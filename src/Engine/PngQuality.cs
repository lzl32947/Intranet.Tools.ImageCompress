using ImageMagick;

namespace ImagePress;

internal static class PngQuality
{
    // A fit-to-screen comparison plus native-size detail crops avoids rewarding
    // blurry text merely because its thumbnail resembles the original.
    internal static double Score(MagickImage original, Encoded encoded)
    {
        using var candidate = new MagickImage(encoded.Bytes);
        double overview = CompareView(original, candidate, -1);
        double details = 0;
        foreach (double position in new[] { .22, .5, .78 })
            details += CompareView(original, candidate, position);
        return .45 * overview + .55 * details / 3;
    }

    private static double CompareView(MagickImage original, MagickImage candidate, double position)
    {
        uint width = position < 0 ? Math.Min(480u, original.Width) : Math.Min(144u, original.Width);
        uint height = position < 0 ? Math.Max(1u, (uint)Math.Round((double)original.Height * width / original.Width)) : Math.Min(144u, original.Height);
        if (position < 0 && height > 480)
        {
            width = Math.Max(1u, (uint)Math.Round(width * 480d / height));
            height = 480;
        }
        using var a = View(original, original, position, width, height);
        using var b = View(candidate, original, position, width, height);
        return Math.Min(OnBackground(a, b, MagickColors.White), OnBackground(a, b, MagickColors.Black));
    }

    private static MagickImage View(MagickImage image, MagickImage reference, double position, uint w, uint h)
    {
        var view = new MagickImage(image);
        try
        {
            if (position >= 0)
            {
                uint cw = Math.Clamp((uint)Math.Round((double)w * view.Width / reference.Width), 1u, view.Width);
                uint ch = Math.Clamp((uint)Math.Round((double)h * view.Height / reference.Height), 1u, view.Height);
                int x = (int)Math.Round((view.Width - cw) * position);
                int y = (int)Math.Round((view.Height - ch) * position);
                view.Crop(new MagickGeometry(x, y, cw, ch));
                view.ResetPage();
            }
            if (view.Width != w || view.Height != h)
                view.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true }, FilterType.Lanczos);
            return view;
        }
        catch { view.Dispose(); throw; }
    }

    private static double OnBackground(MagickImage a, MagickImage b, IMagickColor<ushort> background)
    {
        using var left = new MagickImage(a);
        using var right = new MagickImage(b);
        foreach (var image in new[] { left, right })
        {
            image.BackgroundColor = background;
            image.Alpha(AlphaAction.Remove);
            image.Alpha(AlphaAction.Off);
        }
        if (Math.Min(left.Width, left.Height) < 12)
            return 1 - left.Compare(right, ErrorMetric.RootMeanSquared, Channels.RGB);
        // Magick.NET exposes the distortion (zero for identical images).
        return 1 - left.Compare(right, ErrorMetric.StructuralSimilarity, Channels.RGB);
    }
}
