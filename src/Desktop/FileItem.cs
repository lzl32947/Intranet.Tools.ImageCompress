using System.ComponentModel;
using System.IO;

namespace ImagePress.Desktop;

public sealed class FileItem(string path) : INotifyPropertyChanged
{
    public string PathName { get; } = path;
    public string Name => Path.GetFileName(PathName);
    public ImageResult? Result { get; private set; }
    public string Status { get; private set; } = "待处理";
    public string? Output => Result?.Output;
    public string Sizes => Result is null ? "" : Result.OutputBytes is long n ?
        $"{Size(Result.InputBytes)} → {Size(n)}" : Size(Result.InputBytes);
    public string Pixels => Result is not { } r ? "" : string.IsNullOrEmpty(r.OutputDimensions) || r.OriginalDimensions == r.OutputDimensions ?
        r.OriginalDimensions : $"{r.OriginalDimensions} → {r.OutputDimensions}";
    public string Description => Result is not { } r ? PathName : string.Join(Environment.NewLine,
        new[] { PathName, r.Note, r.InputBytes is long a ? $"原图 {a:N0} 字节 · {r.OriginalDimensions}" : "",
        r.OutputBytes is long b ? $"输出 {b:N0} 字节 · {r.OutputDimensions}" : "", r.Output }.Where(s => !string.IsNullOrEmpty(s)));
    private static string Size(long? n) => n is null ? "" : n < 1000 ? $"{n} B" : n < 1_000_000 ? $"{n / 1000d:0.#} KB" : $"{n / 1_000_000d:0.##} MB";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Set(string status, ImageResult? result = null)
    {
        Result = result;
        Status = status;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
