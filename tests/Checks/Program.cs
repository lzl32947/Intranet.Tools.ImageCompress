using System.Security.Cryptography;
using ImageMagick;
using ImagePress;

if (args.Length != 1) throw new ArgumentException("Pass a fresh directory from make_fixtures.py.");
string folder = Path.GetFullPath(args[0]);
var originals = Directory.GetFiles(folder).ToDictionary(p => p, Hash);
int passed = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS " + description);
    passed++;
}
ImageResult Run(string file, long kb, bool png = false)
{
    var r = Compressor.Run(Path.Combine(folder, file), new(kb, png));
    Console.WriteLine($"{file} @ {kb} -> {r.Outcome} {r.Note} {r.OutputBytes} {r.OriginalDimensions} / {r.OutputDimensions}");
    if (r.Outcome == Outcome.Written)
    {
        var data = new FileInfo(r.Output!);
        using var decoded = new MagickImage(data);
        Check(data.Length == r.OutputBytes && data.Length > 0 && data.Length <= kb * 1000 &&
            decoded.Width > 0 && decoded.Height > 0 && data.DirectoryName == folder, "valid capped sibling " + file);
    }
    return r;
}
static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
using (var black = new MagickImage(MagickColors.Black, 32, 32))
using (var whiteImage = new MagickImage(MagickColors.White, 32, 32))
    Check(PngQuality.Score(black, new(black.ToByteArray(MagickFormat.Png), 32, 32, "")) >
          PngQuality.Score(black, new(whiteImage.ToByteArray(MagickFormat.Png), 32, 32, "")), "PNG visual scoring ranks identical pixels first");
foreach (string file in new[] { "动图.png", "动图.gif", "动图.webp", "动图.avif", "pages.tif" })
    Check(Run(file, 500).Outcome == Outcome.Skipped, "animation/multipage " + file);
Check(Run("已达标.jpeg", 500).Note == "原图已达标" && Run("small.png", 1000).Output == null, "small original skipped");
Check(Run("正好500KB.jpg", 500).Outcome == Outcome.Skipped && Run("多一字节.jpg", 500).Outcome == Outcome.Written, "exact decimal cap and one byte over");
Check(Run("损坏.png", 500).Outcome == Outcome.Failed, "invalid image reported");
foreach (string file in new[] { "phone.heic", "phone.heif", "sample.avif", "sample.tiff", "sample.gif", "sample.bmp" })
    Check(Run(file, 500).Output?.EndsWith(".jpg") == true, "format conversion " + file);
foreach (long kb in new long[] { 500, 1000, 75 })
{
    var photo = Run("手机 竖拍.jpg", kb);
    Check(photo.Outcome != Outcome.Failed, "oriented JPEG " + kb);
    if (photo.Output is not null)
    {
        using var outImage = new MagickImage(photo.Output);
        outImage.AutoOrient();
        Check(outImage.Height > outImage.Width, "upright portrait " + kb);
    }
}
var detail = Run("高细节.jpg", 70);
// Construct a byte cap between the original and a genuine JPEG coefficient optimization.
using (var buffer = new MemoryStream())
{
    byte[] bytes = File.ReadAllBytes(Path.Combine(folder, "手机 竖拍.jpg"));
    buffer.Write(bytes); buffer.Position = 0;
    if (new ImageOptimizer().LosslessCompress(buffer) && bytes.LongLength - buffer.Length > 2000)
    {
        var lossless = Run("手机 竖拍.jpg", (bytes.LongLength + buffer.Length) / 2000);
        using var src = new MagickImage(bytes);
        using var dst = new MagickImage(lossless.Output!);
        Check(lossless.Note == "无损优化" && src.Compare(dst, ErrorMetric.Absolute, Channels.RGBA) == 0 &&
            src.Orientation == dst.Orientation && src.GetColorProfile()!.ToByteArray().SequenceEqual(dst.GetColorProfile()!.ToByteArray()),
            "lossless JPEG coefficients, orientation and ICC");
    }
}
Check(detail.Outcome == Outcome.Written && detail.OriginalDimensions != detail.OutputDimensions, "proportional JPEG resize");
var tiny = Run("高细节.jpg", 1);
Check(tiny.Outcome == Outcome.Written, "1 KB custom");
var png16 = Run("十六位.png", 100);
using (var orig = new MagickImage(Path.Combine(folder, "十六位.png")))
using (var encoded = new MagickImage(png16.Output!))
    Check(encoded.Depth == 16 && orig.Compare(encoded, ErrorMetric.Absolute) == 0, "16-bit lossless PNG pixels");
var screenshot = Run("小字截图.png", 100);
Check(screenshot.Outcome == Outcome.Written && screenshot.OriginalDimensions == screenshot.OutputDimensions, "screenshot resolution retained");
var alpha = Run("透明 边缘.png", 14);
using (var encoded = new MagickImage(alpha.Output!))
{
    var colors = encoded.GetPixels().ToByteArray(PixelMapping.RGBA)!;
    var alphas = Enumerable.Range(0, colors.Length / 4).Select(i => colors[i * 4 + 3]).ToArray();
    Check(alphas.Min() == 0 && alphas.Any(a => a is > 3 and < 252), "transparent and semitransparent pixels");
}
var white = Run("alpha.webp", 500);
using (var jpeg = new MagickImage(white.Output!))
{
    var pixel = jpeg.GetPixels().GetPixel(0, 0).ToColor()!;
    Check(pixel.R > 63500 && pixel.G > 63500 && pixel.B > 63500, "transparent JPG uses white");
}
var pngConversion = Run("alpha.webp", 500, true);
using (var converted = new MagickImage(pngConversion.Output!)) Check(converted.HasAlpha, "transparent WebP to PNG");
var concurrent = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => Compressor.Run(
    Path.Combine(folder, "alpha.webp"), new(500, false)))));
Check(concurrent.All(x => x.Outcome == Outcome.Written) && concurrent.Select(x => x.Output).Distinct().Count() == 3 &&
      concurrent.Any(x => x.Output!.Contains("(2)")), "concurrent collision-safe naming");
var state = new UserState(Path.Combine(folder, "test-state"));
Check(state.Read() is null, "fresh preferences");
state.Begin(new(300, true));
Check(state.Begin(preset: 1000) == new RunOptions(1000, true), "preset inherits the selected format");
Check(new UserState(Path.Combine(folder, "test-state")).Begin(requirePrevious: true) == new RunOptions(1000, true),
    "300 KB to 1000 KB to relaunch to repeat");
var id = state.QueueSelection(originals.Keys.ToList());
Check(state.TakeSelection(id).SequenceEqual(originals.Keys), "Unicode multiselect request roundtrip");
Check(originals.All(p => Hash(p.Key) == p.Value), "all originals byte-identical");
Check(Directory.GetFiles(folder, ".imagepress-*.tmp").Length == 0, "no partial files");
Console.WriteLine($"{passed} checks passed.");
