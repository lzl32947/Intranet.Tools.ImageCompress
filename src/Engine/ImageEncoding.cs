using ImageMagick;

namespace ImagePress;

internal static class ImageEncoding
{
    private delegate Encoded Probe(uint edge);

    internal static Encoded Jpeg(MagickImage source, long cap, CancellationToken cancel)
    {
        uint edge = Math.Max(source.Width, source.Height);
        var baseline = LargestFitting(edge, cap, e =>
        {
            using var copy = AtSize(source, e);
            return EncodeJpeg(copy, 75, cancel);
        }, cancel);
        using var final = AtSize(source, Math.Max(baseline.Width, baseline.Height));
        // Descending search also handles small non-monotonic changes in JPEG size.
        for (int q = 100; q > 75; q--)
        {
            var attempt = EncodeJpeg(final, q, cancel);
            if (attempt.Bytes.LongLength <= cap) return attempt;
        }
        return baseline;
    }

    private static Encoded EncodeJpeg(MagickImage image, int quality, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        image.Quality = (uint)quality;
        image.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", true);
        return new(image.ToByteArray(MagickFormat.Jpeg), image.Width, image.Height, $"JPG 质量 {quality}");
    }

    internal static Encoded Png(MagickImage source, long cap, CancellationToken cancel)
    {
        uint edge = Math.Max(source.Width, source.Height);
        var full = LargestFitting(edge, cap, e =>
        {
            cancel.ThrowIfCancellationRequested();
            using var copy = AtSize(source, e);
            copy.Settings.SetDefine(MagickFormat.Png, "compression-level", 9);
            copy.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "date,time");
            return new(copy.ToByteArray(MagickFormat.Png), copy.Width, copy.Height,
                e == edge ? "原尺寸 PNG" : "等比缩小 PNG");
        }, cancel);
        if (Math.Max(full.Width, full.Height) == edge) return full;

        foreach (uint colors in new uint[] { 256, 128, 64, 32, 16, 8, 4, 2 })
        {
            cancel.ThrowIfCancellationRequested();
            using var reduced = new MagickImage(source);
            reduced.Quantize(new QuantizeSettings { Colors = colors, DitherMethod = DitherMethod.No });
            reduced.Depth = 8;
            // Palette averaging can turn invisible margins into a faint colored
            // haze. Keep the source coverage at every pixel for this candidate.
            if (source.HasAlpha)
            {
                reduced.Composite(source, CompositeOperator.CopyAlpha);
                reduced.Depth = source.Depth > 8 ? 16u : 8u;
            }
            reduced.Settings.SetDefine(MagickFormat.Png, "compression-level", 9);
            reduced.Settings.SetDefine(MagickFormat.Png, "exclude-chunk", "date,time");
            // Generic PNG preserves palette entries with partial alpha (tRNS).
            var palette = new Encoded(reduced.ToByteArray(MagickFormat.Png), reduced.Width, reduced.Height, $"原尺寸减色 · {colors} 色");
            if (palette.Bytes.LongLength > cap) continue;
            cancel.ThrowIfCancellationRequested();
            return PngQuality.Score(source, palette) >= PngQuality.Score(source, full) ? palette : full;
        }
        return full;
    }

    private static MagickImage AtSize(MagickImage source, uint edge)
    {
        var copy = new MagickImage(source);
        try
        {
            if (edge < Math.Max(copy.Width, copy.Height))
            {
                copy.Resize(edge, edge, FilterType.Lanczos);
                copy.ResetPage();
            }
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    private static Encoded LargestFitting(uint edge, long cap, Probe probe, CancellationToken cancel)
    {
        uint current = edge, tooLarge = edge;
        Encoded best;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            var attempt = probe(current);
            if (attempt.Bytes.LongLength <= cap) { best = attempt; break; }
            if (current == 1) throw new InvalidOperationException("指定上限太小，无法容纳有效图片。");
            tooLarge = current;
            double ratio = Math.Clamp(Math.Sqrt((double)cap / attempt.Bytes.LongLength) * .97, .12, .94);
            current = Math.Clamp((uint)(current * ratio), 1u, current - 1);
        }
        // Recover resolution after the first estimate. Every accepted candidate
        // has been encoded and measured at its actual final geometry.
        for (int i = 0; i < 7 && tooLarge - current > 1; i++)
        {
            cancel.ThrowIfCancellationRequested();
            uint mid = current + (tooLarge - current) / 2;
            var attempt = probe(mid);
            if (attempt.Bytes.LongLength <= cap) { current = mid; best = attempt; }
            else tooLarge = mid;
        }
        return best;
    }
}
