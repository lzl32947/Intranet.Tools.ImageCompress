using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ImagePress;

public sealed record RunOptions(long Kilobytes, bool OtherToPng)
{
    public static RunOptions Default { get; } = new(500, false);
    public long ByteLimit => checked(Kilobytes * 1000);
    public static bool IsValid(long value) => value > 0 && value <= long.MaxValue / 1000;
}

public sealed class UserState(string directory)
{
    private const string Family = "ImagePress.Desktop_xnxz7mp0jqdbm";
    private string SettingsFile => Path.Combine(directory, "last-run.txt");

    // A packaged WPF process and a COM surrogate can otherwise receive different
    // redirected LocalAppData locations. Both use the package's actual LocalState.
    public static UserState ForUser()
    {
        Guid local = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
        Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(ref local, 0x00010000, 0, out nint path));
        try { return new(Path.Combine(Marshal.PtrToStringUni(path)!, "Packages", Family, "LocalState")); }
        finally { Marshal.FreeCoTaskMem(path); }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);

    public RunOptions? Read()
    {
        try
        {
            using var file = new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 80) return null;
            using var text = new StreamReader(file, Encoding.UTF8);
            if (!long.TryParse(text.ReadLine(), NumberStyles.None, CultureInfo.InvariantCulture, out long size) || !RunOptions.IsValid(size))
                return null;
            return text.ReadLine() switch { "png" => new(size, true), "jpg" => new(size, false), _ => null };
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // Resolve a preset and save its inherited format in the same cross-process operation.
    public RunOptions Begin(RunOptions? configured = null, long? preset = null, bool requirePrevious = false)
    {
        using var mutex = new Mutex(false, @"Local\ImagePress-9695A54C-StartSettings");
        try { mutex.WaitOne(); } catch (AbandonedMutexException) { }
        try
        {
            var previous = Read();
            if (requirePrevious && previous is null) throw new InvalidOperationException("尚无上次参数，请先选择一个压缩档位。");
            var chosen = configured ?? previous ?? RunOptions.Default;
            if (preset is long target) chosen = chosen with { Kilobytes = target };
            if (!RunOptions.IsValid(chosen.Kilobytes)) throw new ArgumentOutOfRangeException(nameof(configured));
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllLines(temporary, [chosen.Kilobytes.ToString(CultureInfo.InvariantCulture), chosen.OtherToPng ? "png" : "jpg"]);
                File.Move(temporary, SettingsFile, true);
            }
            finally { File.Delete(temporary); }
            return chosen;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private string JobPath(Guid id) => Path.Combine(directory, "requests", id.ToString("N") + ".lst");

    public Guid QueueSelection(IReadOnlyList<string> paths)
    {
        Guid id = Guid.NewGuid();
        string file = JobPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try { File.WriteAllLines(file, paths, new UTF8Encoding(false)); }
        catch { File.Delete(file); throw; }
        return id;
    }

    public string[] TakeSelection(Guid id)
    {
        string file = JobPath(id);
        try { return File.ReadAllLines(file); }
        finally { File.Delete(file); }
    }

    public void DiscardSelection(Guid id) => File.Delete(JobPath(id));
}

public static class ImageTypes
{
    public const string FileFilter = "图片|*.jpg;*.jpeg;*.jfif;*.png;*.apng;*.webp;*.bmp;*.gif;*.tif;*.tiff;*.heic;*.heif;*.avif";
    public static bool Includes(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".jpg" or ".jpeg" or ".jfif" or ".png" or ".apng" or ".webp" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".heic" or ".heif" or ".avif";
}
