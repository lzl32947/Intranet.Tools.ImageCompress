using System.Buffers.Binary;
using ImageMagick;

namespace ImagePress;

public enum Outcome { Written, Skipped, Failed }
public sealed record ImageResult(string Source, Outcome Outcome, string Note, long? InputBytes = null,
    long? OutputBytes = null, string OriginalDimensions = "", string OutputDimensions = "", string? Output = null);
internal sealed record Encoded(byte[] Bytes, uint Width, uint Height, string Method);

public static class Compressor
{
    public static ImageResult Run(string path, RunOptions options, CancellationToken cancel = default)
    {
        long? length = null;
        string originalSize = "";
        try
        {
            cancel.ThrowIfCancellationRequested();
            if (!RunOptions.IsValid(options.Kilobytes)) throw new ArgumentException("目标大小必须为正整数 KB。");
            path = Path.GetFullPath(path);
            var inputFormat = DecoderFor(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            length = file.Length;
            var settings = new MagickReadSettings { Format = inputFormat };
            bool animatedPng = inputFormat == MagickFormat.Png && IsAnimatedPng(file);

            using (var frames = new MagickImageCollection())
            {
                frames.Ping(file, settings);
                if (frames.Count == 0) throw new InvalidDataException("未读到图像。");
                var first = frames[0];
                (uint w, uint h) = DisplayDimensions(first);
                originalSize = Dimensions(w, h);
                if (animatedPng)
                    return new(path, Outcome.Skipped, "动画 PNG 已跳过", length, OriginalDimensions: originalSize);
                if (frames.Count > 1 || (inputFormat == MagickFormat.WebP && IsAnimatedWebP(file)))
                    return new(path, Outcome.Skipped, "动画或多页图片已跳过", length, OriginalDimensions: originalSize);
            }

            bool jpeg = inputFormat == MagickFormat.Jpeg;
            bool png = inputFormat == MagickFormat.Png;
            bool writePng = png || (!jpeg && options.OtherToPng);
            if ((jpeg || png) && length <= options.ByteLimit)
                return new(path, Outcome.Skipped, "原图已达标", length, OriginalDimensions: originalSize);

            file.Position = 0;
            using var source = new MagickImage(file, settings);
            var display = DisplayDimensions(source);
            originalSize = Dimensions(display.Width, display.Height);
            Encoded? best = jpeg || png ? TryLossless(file, source, options.ByteLimit, cancel) : null;
            if (best is null)
            {
                source.AutoOrient();
                source.ResetPage();
                // Pixel orientation is now baked in. Remove stale thumbnails and
                // orientation tags, retaining color profiles and transfer information.
                foreach (string profile in source.ProfileNames.Where(x =>
                    !x.Equals("icc", StringComparison.OrdinalIgnoreCase) &&
                    !x.Equals("icm", StringComparison.OrdinalIgnoreCase)).ToArray())
                    source.RemoveProfile(profile);
                source.Orientation = Orientation.TopLeft;
                if (source.ColorSpace == ColorSpace.CMYK && writePng)
                {
                    if (source.GetColorProfile() is not null) source.TransformColorSpace(ColorProfiles.SRGB);
                    else source.ColorSpace = ColorSpace.sRGB;
                }
                if (!writePng && source.HasAlpha)
                {
                    source.BackgroundColor = MagickColors.White;
                    source.Alpha(AlphaAction.Remove);
                    source.Alpha(AlphaAction.Off);
                }
                // Very small targets can be smaller than the entire ICC payload.
                // Convert the actual colors to sRGB before omitting that profile.
                if (source.GetColorProfile() is { } icc && icc.ToByteArray().LongLength + 700 >= options.ByteLimit)
                {
                    source.TransformColorSpace(ColorProfiles.SRGB);
                    source.RemoveProfile("icc");
                    source.RemoveProfile("icm");
                }
                best = writePng ? ImageEncoding.Png(source, options.ByteLimit, cancel)
                                : ImageEncoding.Jpeg(source, options.ByteLimit, cancel);
            }

            string ext = writePng ? ".png" : jpeg && Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpeg" : ".jpg";
            string output = Commit(path, ext, options, best.Bytes, cancel);
            return new(path, Outcome.Written, best.Method, length, best.Bytes.LongLength,
                originalSize, Dimensions(best.Width, best.Height), output);
        }
        catch (OperationCanceledException) { throw; }
        catch (OutOfMemoryException)
        {
            return new(path, Outcome.Failed, "可用内存不足，无法处理这张图片。", length, OriginalDimensions: originalSize);
        }
        catch (Exception ex)
        {
            return new(path, Outcome.Failed, ex.Message, length, OriginalDimensions: originalSize);
        }
    }

    private static MagickFormat DecoderFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".jfif" => MagickFormat.Jpeg,
        ".png" or ".apng" => MagickFormat.Png, ".webp" => MagickFormat.WebP, ".bmp" => MagickFormat.Bmp,
        ".gif" => MagickFormat.Gif, ".tif" or ".tiff" => MagickFormat.Tiff,
        ".heic" or ".heif" => MagickFormat.Heic, ".avif" => MagickFormat.Avif,
        _ => throw new InvalidDataException("不支持此文件格式。")
    };

    private static (uint Width, uint Height) DisplayDimensions(IMagickImage<ushort> image) =>
        image.Orientation is Orientation.LeftTop or Orientation.LeftBottom or Orientation.RightTop or Orientation.RightBottom
        ? (image.Height, image.Width) : (image.Width, image.Height);
    private static string Dimensions(uint w, uint h) => $"{w} × {h}";

    private static Encoded? TryLossless(Stream original, MagickImage source, long limit, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        // The optimizer requires a writable, expandable stream. Large originals
        // use temporary storage so the optimizer does not duplicate them in RAM.
        using Stream optimized = original.Length < 32L * 1024 * 1024 ? new MemoryStream() :
            new FileStream(Path.Combine(Path.GetTempPath(), $"imagepress-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        original.Position = 0;
        original.CopyTo(optimized);
        optimized.Position = 0;
        try { new ImageOptimizer().LosslessCompress(optimized); }
        catch (MagickException) { return null; }
        if (optimized.Length == 0 || optimized.Length > limit || optimized.Length > Array.MaxLength) return null;
        optimized.Position = 0;
        using var check = new MagickImage(optimized);
        var (w, h) = DisplayDimensions(check);
        if (DisplayDimensions(source) != (w, h) || source.Orientation != check.Orientation ||
            !ProfilesMatch(source, check) || source.Depth != check.Depth ||
            source.Compare(check, ErrorMetric.Absolute, Channels.RGBA) != 0)
            return null;
        var encoded = new byte[(int)optimized.Length];
        optimized.Position = 0;
        optimized.ReadExactly(encoded);
        return new(encoded, w, h, "无损优化");
    }

    private static bool ProfilesMatch(MagickImage a, MagickImage b)
    {
        byte[]? left = a.GetColorProfile()?.ToByteArray(), right = b.GetColorProfile()?.ToByteArray();
        return (left is null) == (right is null) && left.AsSpan().SequenceEqual(right) && a.ColorSpace == b.ColorSpace && a.Gamma == b.Gamma;
    }

    private static bool IsAnimatedPng(Stream file)
    {
        Span<byte> header = stackalloc byte[8];
        try
        {
            file.Position = 8;
            while (file.Length - file.Position >= 12)
            {
                file.ReadExactly(header);
                uint size = BinaryPrimitives.ReadUInt32BigEndian(header);
                if (header[4..].SequenceEqual("acTL"u8)) return true;
                if (header[4..].SequenceEqual("IDAT"u8) || header[4..].SequenceEqual("IEND"u8)) break;
                if ((long)size + 4 > file.Length - file.Position) throw new InvalidDataException("PNG 数据块不完整。");
                file.Seek((long)size + 4, SeekOrigin.Current);
            }
            return false;
        }
        finally { file.Position = 0; }
    }

    private static bool IsAnimatedWebP(Stream file)
    {
        if (file.Length < 21) return false;
        file.Position = 12;
        Span<byte> chunk = stackalloc byte[9];
        file.ReadExactly(chunk);
        file.Position = 0;
        return chunk[..4].SequenceEqual("VP8X"u8) && (chunk[8] & 2) != 0;
    }

    private static string Commit(string source, string extension, RunOptions options, byte[] data, CancellationToken cancel)
    {
        string folder = Path.GetDirectoryName(source)!;
        string stem = Path.GetFileNameWithoutExtension(source) + "_" + options.Kilobytes + "KB";
        string temporary = Path.Combine(folder, ".imagepress-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(data);
                if (output.Length == 0 || output.Length > options.ByteLimit)
                    throw new InvalidDataException("编码结果未达到指定上限。");
            }
            for (int number = 1; ; number++)
            {
                cancel.ThrowIfCancellationRequested();
                string suffix = number == 1 ? "" : $" ({number})";
                string candidate = Path.Combine(folder, stem + suffix + extension);
                try { File.Move(temporary, candidate, false); return candidate; }
                catch (IOException ex) when ((ex.HResult & 0xffff) is 80 or 183 || File.Exists(candidate) || Directory.Exists(candidate)) { }
            }
        }
        finally { File.Delete(temporary); }
    }
}
