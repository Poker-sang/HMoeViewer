using SQLite;

namespace HMoeData.Persistence;

// Explicit scalar rows keep the crawler's existing TEXT dates/URIs and join tables compatible.
[Table("Posts")]
internal sealed class PostRow
{
    [PrimaryKey]
    public int Id { get; set; }

    [NotNull]
    public string Title { get; set; } = "";

    [NotNull]
    public string Url { get; set; } = "";

    [NotNull]
    public string Slug { get; set; } = "";

    public int CommentsCount { get; set; }

    [NotNull]
    public string Type { get; set; } = "";

    [NotNull]
    public string Excerpt { get; set; } = "";

    [NotNull]
    public string Content { get; set; } = "";

    [NotNull]
    public string Date { get; set; } = "";

    [NotNull]
    public string ModifiedDate { get; set; } = "";

    public int Views { get; set; }

    [NotNull]
    public string WriteTime { get; set; } = "";

    public int IsSelected { get; set; }

    public int DbAuthorId { get; set; }

    public long DateUnixTimeSeconds { get; set; }

    public long ModifiedDateUnixTimeSeconds { get; set; }

    public long WriteTimeUnixTimeSeconds { get; set; }
}

[Table("Posts")]
internal sealed class PostSelectionRow
{
    [PrimaryKey]
    public int Id { get; set; }

    public int IsSelected { get; set; }
}

[Table("Authors")]
internal sealed class AuthorRow
{
    [PrimaryKey]
    public int Id { get; set; }

    [NotNull]
    public string Uid { get; set; } = "";

    [NotNull]
    public string Name { get; set; } = "";

    [NotNull]
    public string Url { get; set; } = "";

    [NotNull]
    public string AvatarUrl { get; set; } = "";

    [NotNull]
    public string Des { get; set; } = "";

    public int CommentsCount { get; set; }

    public int PostsCount { get; set; }

    public int Point { get; set; }

    public int FollowersCount { get; set; }

    public int FansCount { get; set; }

    [NotNull]
    public string DbRoleName { get; set; } = "";
}

[Table("Roles")]
internal sealed class RoleRow
{
    [PrimaryKey]
    [NotNull]
    public string Name { get; set; } = "";

    [NotNull]
    public string Color { get; set; } = "";
}

[Table("Thumbnails")]
internal sealed class ThumbnailRow
{
    [PrimaryKey]
    public int DbPostId { get; set; }

    [NotNull]
    public string Id { get; set; } = "";

    [NotNull]
    public string Width { get; set; } = "";

    [NotNull]
    public string Height { get; set; } = "";

    [NotNull]
    public string Url { get; set; } = "";

    public bool Visible { get; set; }
}

[Table("Tags")]
internal sealed class TagRow
{
    [PrimaryKey]
    public int Id { get; set; }

    [NotNull]
    public string Name { get; set; } = "";

    [NotNull]
    public string Slug { get; set; } = "";

    [NotNull]
    public string Url { get; set; } = "";

    [NotNull]
    public string Description { get; set; } = "";

    public int Count { get; set; }
}

[Table("Categories")]
internal sealed class CategoryRow
{
    [PrimaryKey]
    public int Id { get; set; }

    public int ParentId { get; set; }

    [NotNull]
    public string Name { get; set; } = "";

    [NotNull]
    public string Slug { get; set; } = "";

    [NotNull]
    public string Uuid { get; set; } = "";

    [NotNull]
    public string Url { get; set; } = "";

    [NotNull]
    public string Description { get; set; } = "";
}

[Table("Medals")]
internal sealed class MedalRow
{
    [PrimaryKey]
    [NotNull]
    public string Id { get; set; } = "";

    [NotNull]
    public string ImgUrl { get; set; } = "";

    [NotNull]
    public string Name { get; set; } = "";

    [NotNull]
    public string Des { get; set; } = "";

    [NotNull]
    public string AttrTitle { get; set; } = "";
}

[Table("PostTag")]
internal sealed class PostTagRow
{
    public int PostId { get; set; }

    public int DbTagsId { get; set; }
}

[Table("CategoryPost")]
internal sealed class CategoryPostRow
{
    public int PostId { get; set; }

    public int DbCatsId { get; set; }
}

[Table("AuthorMedal")]
internal sealed class AuthorMedalRow
{
    public int AuthorId { get; set; }

    [NotNull]
    public string DbMedalsId { get; set; } = "";
}
