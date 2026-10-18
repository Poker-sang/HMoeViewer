using System;
using System.Collections.Generic;
using Avalonia;
using ColorTextBlock.Avalonia;
using ColorTextBlock.Avalonia.Geometries;
using HMoeViewer.Core;
using HtmlAgilityPack;
using Markdown.Avalonia.Html.Core;
using Markdown.Avalonia.Html.Core.Parsers;

namespace HMoeViewer.Controls;

internal sealed class ArticleImageParser(Func<CachedArticle?> article) : IInlineTagParser, IHasPriority
{
    public int Priority => -100;

    public IEnumerable<string> SupportTag => ["img"];

    bool ITagParser.TryReplace(HtmlNode node, ReplaceManager manager, out IEnumerable<StyledElement> generated)
    {
        _ = TryReplace(node, manager, out var inlines);
        generated = inlines;
        return true;
    }

    public bool TryReplace(HtmlNode node, ReplaceManager manager, out IEnumerable<CInline> generated)
    {
        var source = HtmlEntity.DeEntitize(node.GetAttributeValue("src", ""));
        generated = [new ArticleImageInline(new(source, article()))];
        return true;
    }

    private sealed class ImageLineBreak : CLineBreak
    {
        public IEnumerable<CGeometry> MeasureLineBreak(double entireWidth, double remainWidth) =>
            base.MeasureOverride(entireWidth, remainWidth);
    }

    private sealed class ArticleImageInline : CInlineUIContainer
    {
        protected override IEnumerable<CGeometry> MeasureOverride(double entireWidth, double remainWidth)
        {
            // UI controls clip DesiredSize to the available width, hiding overflow from
            // CInlineUIContainer. Decide the line break before that measurement occurs.
            if (Content is ArticleImage image && Math.Min(image.Width, entireWidth) > remainWidth
                && remainWidth < entireWidth)
            {
                foreach (var geometry in new ImageLineBreak().MeasureLineBreak(entireWidth, remainWidth))
                    yield return geometry;
                remainWidth = entireWidth;
            }
            foreach (var geometry in base.MeasureOverride(entireWidth, remainWidth))
                yield return geometry;
        }

        public ArticleImageInline(ArticleImage image) : base(image)
        {
            // Inline geometry is cached separately from the child control's layout.
            // A downloaded image must replace the placeholder's measured line height.
            image.PropertyChanged += (_, change) =>
            {
                if (change.Property == ArticleImage.WidthProperty
                    || change.Property == ArticleImage.HeightProperty
                    || change.Property == ArticleImage.ChildProperty)
                    RequestMeasure();
            };
        }
    }
}
