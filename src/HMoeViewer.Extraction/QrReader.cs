using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using ZXing;
using ZXing.Common;

namespace HMoeViewer.Extraction;

public sealed class QrReader(string models) : IDisposable
{
    private readonly WeChatQRCode _detector = new(Path.Combine(models, "detect.prototxt"), Path.Combine(models, "detect.caffemodel"),
        Path.Combine(models, "sr.prototxt"), Path.Combine(models, "sr.caffemodel"));
    private readonly BarcodeReaderGeneric _reader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = [BarcodeFormat.QR_CODE] }
    };

    public string[] Decode(byte[] bytes)
    {
        using var image = Cv2.ImDecode(bytes, ImreadModes.Color);
        if (image.Empty())
            throw new InvalidDataException("图片格式不受 OpenCV 支持或内容损坏。");
        var results = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in _detector.DetectAndDecode(image, out _))
            if (!string.IsNullOrWhiteSpace(text))
                _ = results.Add(text);
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        var pixels = new byte[gray.Rows * gray.Cols];
        System.Runtime.InteropServices.Marshal.Copy(gray.Data, pixels, 0, pixels.Length);
        var source = new RGBLuminanceSource(pixels, gray.Cols, gray.Rows, RGBLuminanceSource.BitmapFormat.Gray8);
        foreach (var result in _reader.DecodeMultiple(source) ?? [])
            _ = results.Add(result.Text);
        return [.. results];
    }

    public void Dispose()
    {
        _detector.Dispose();
        GC.SuppressFinalize(this);
    }
}
