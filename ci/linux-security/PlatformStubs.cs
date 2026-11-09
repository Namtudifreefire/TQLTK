namespace System.Windows.Media
{
    internal readonly struct PixelFormat;

    internal static class PixelFormats
    {
        internal static PixelFormat Bgra32 => default;
    }

    internal sealed class ScaleTransform
    {
        internal ScaleTransform(double scaleX, double scaleY) { }
    }

    internal sealed class TransformedBitmap : System.Windows.Media.Imaging.BitmapSource
    {
        internal TransformedBitmap(System.Windows.Media.Imaging.BitmapSource source, ScaleTransform transform) { }
    }

    internal sealed class FormatConvertedBitmap : System.Windows.Media.Imaging.BitmapSource
    {
        internal FormatConvertedBitmap(System.Windows.Media.Imaging.BitmapSource source, PixelFormat format, object? palette, double alphaThreshold) { }
    }
}

namespace System.Windows.Media.Imaging
{
    internal enum BitmapCreateOptions
    {
        PreservePixelFormat
    }

    internal enum BitmapCacheOption
    {
        OnLoad
    }

    internal class BitmapSource
    {
        internal virtual int PixelWidth => 0;
        internal virtual int PixelHeight => 0;
        internal virtual void Freeze() { }
        internal virtual void CopyPixels(byte[] pixels, int stride, int offset) => throw new PlatformNotSupportedException();
    }

    internal sealed class BitmapFrame : BitmapSource;

    internal sealed class BitmapDecoder
    {
        internal IReadOnlyList<BitmapFrame> Frames => Array.Empty<BitmapFrame>();

        internal static BitmapDecoder Create(Stream stream, BitmapCreateOptions createOptions, BitmapCacheOption cacheOption) =>
            throw new PlatformNotSupportedException("QR image decoding is outside the portable TOTP algorithm suite.");
    }
}

namespace ZXing
{
    internal enum BarcodeFormat
    {
        QR_CODE
    }

    internal abstract class LuminanceSource;

    internal sealed class Result
    {
        internal string Text { get; init; } = "";
    }

    internal sealed class DecodingOptions
    {
        internal bool TryHarder { get; set; }
        internal IList<BarcodeFormat> PossibleFormats { get; set; } = new List<BarcodeFormat>();
    }

    internal sealed class BarcodeReaderGeneric
    {
        internal bool AutoRotate { get; set; }
        internal DecodingOptions Options { get; } = new();
        internal Result? Decode(LuminanceSource source) =>
            throw new PlatformNotSupportedException("QR image decoding is outside the portable TOTP algorithm suite.");
    }
}

namespace ZXing.Common
{
    internal sealed class RGBLuminanceSource : ZXing.LuminanceSource
    {
        internal enum BitmapFormat
        {
            BGRA32
        }

        internal RGBLuminanceSource(byte[] pixels, int width, int height, BitmapFormat format) { }
    }
}
