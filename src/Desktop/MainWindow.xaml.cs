using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;

namespace ImagePress.Desktop;

public partial class MainWindow : Window
{
    private readonly UserState state;
    private readonly string action;
    private readonly ObservableCollection<FileItem> files = [];
    private CancellationTokenSource? cancellation;
    private bool closeWhenStopped, initialized, finished;
    private bool Editable => action == "main";

    public MainWindow(UserState state, string action, IEnumerable<string> paths)
    {
        this.state = state;
        this.action = action;
        InitializeComponent();
        FileGrid.ItemsSource = files;
        SetOptions(state.Read() ?? RunOptions.Default);
        initialized = true;
        AddPaths(paths);
        if (!Editable)
        {
            ListActions.Visibility = Visibility.Collapsed;
            if (action == "configure")
            {
                Width = 640; Height = 395;
                ListArea.Visibility = ListToolbar.Visibility = Visibility.Collapsed;
                Heading.Text = "压缩选中的图片";
                Secondary.Content = "取消";
            }
            else SettingsPanel.Visibility = Visibility.Collapsed;
        }
        RefreshControls();
        Loaded += async (_, _) =>
        {
            if (action is "repeat" or "500" or "1000") await RunBatch();
        };
    }

    private void SetOptions(RunOptions options)
    {
        CustomSize.Text = options.Kilobytes.ToString(CultureInfo.InvariantCulture);
        if (options.Kilobytes == 500) Pick500.IsChecked = true;
        else if (options.Kilobytes == 1000) Pick1000.IsChecked = true;
        else PickCustom.IsChecked = true;
        OtherFormat.SelectedIndex = options.OtherToPng ? 1 : 0;
        CustomPanel.Visibility = PickCustom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var seen = files.Select(x => x.PathName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string value in paths)
        {
            if (!ImageTypes.Includes(value)) continue;
            string path = Path.GetFullPath(value);
            if (seen.Add(path)) files.Add(new(path));
        }
        RefreshControls();
    }
    private void RefreshControls()
    {
        if (!initialized) return;
        bool idle = cancellation is null;
        EmptyHint.Visibility = files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectionSummary.Text = Editable ? $"已选择 {files.Count} 张图片" :
            files.Count == 1 ? files[0].Name : $"已选择 {files.Count} 张图片";
        RemoveButton.IsEnabled = idle && FileGrid.SelectedItems.Count > 0;
        AddButton.IsEnabled = idle;
        SettingsPanel.IsEnabled = idle;
        StartButton.IsEnabled = idle && files.Count > 0;
    }
    private void PresetChanged(object sender, RoutedEventArgs e)
    {
        if (CustomPanel is null) return;
        CustomPanel.Visibility = PickCustom.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (initialized) Feedback.Text = "";
    }
    private void SizeEdited(object sender, TextChangedEventArgs e)
    { if (initialized && cancellation is null) Feedback.Text = ""; }
    private void SelectionChangedInList(object sender, SelectionChangedEventArgs e) => RefreshControls();
    private void AddFiles(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择图片", Filter = ImageTypes.FileFilter, Multiselect = true };
        if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
    }
    private void RemoveFiles(object sender, RoutedEventArgs e)
    {
        foreach (var row in FileGrid.SelectedItems.Cast<FileItem>().ToArray()) files.Remove(row);
        RefreshControls();
    }
    private void DragFiles(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = Editable && cancellation is null && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void DropFiles(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        e.Handled = true;
        if (Editable && cancellation is null) AddPaths(paths);
    }
    private async void StartClick(object sender, RoutedEventArgs e) => await RunBatch();
    private async Task RunBatch()
    {
        if (cancellation is not null || files.Count == 0) return;
        RunOptions options;
        try
        {
            if (action is "500" or "1000") options = state.Begin(preset: long.Parse(action, CultureInfo.InvariantCulture));
            else if (action == "repeat") options = state.Begin(requirePrevious: true);
            else
            {
                long kb = Pick500.IsChecked == true ? 500 : Pick1000.IsChecked == true ? 1000 : 0;
                if (PickCustom.IsChecked == true && (!long.TryParse(CustomSize.Text.Trim(), NumberStyles.None,
                    CultureInfo.InvariantCulture, out kb) || !RunOptions.IsValid(kb)))
                { Feedback.Text = "请输入有效的正整数 KB。"; CustomSize.Focus(); return; }
                options = state.Begin(new RunOptions(kb, OtherFormat.SelectedIndex == 1));
            }
        }
        catch (Exception ex) { Feedback.Text = ex.Message; return; }

        cancellation = new();
        var token = cancellation.Token;
        try
        {
            finished = false;
            SetOptions(options);
            if (!Editable)
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                ListToolbar.Visibility = ListArea.Visibility = Visibility.Visible;
                Width = Math.Min(1000, SystemParameters.WorkArea.Width);
                Height = Math.Min(520, SystemParameters.WorkArea.Height);
                Left = Math.Clamp(Left, SystemParameters.WorkArea.Left, Math.Max(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Right - Width));
                Top = Math.Clamp(Top, SystemParameters.WorkArea.Top, Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - Height));
                StartButton.Visibility = Visibility.Collapsed;
            }
            RefreshControls();
            Progress.Maximum = files.Count;
            Progress.Value = 0;
            Progress.IsIndeterminate = files.Count == 1;
            Progress.Visibility = Visibility.Visible;
            Secondary.Content = "停止";
            Heading.Text = $"正在压缩至 {options.Kilobytes} KB";
            foreach (var item in files) item.Set("待处理");
            for (int i = 0; i < files.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var item = files[i];
                item.Set("处理中");
                Feedback.Text = $"{i + 1} / {files.Count}    {item.Name}";
                var result = await Task.Run(() => Compressor.Run(item.PathName, options, token), token);
                string status = result.Outcome switch { Outcome.Written => "已保存", Outcome.Skipped => result.Note, _ => "失败" };
                item.Set(status, result);
                Progress.Value = i + 1;
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var item in files.Where(x => x.Result is null)) item.Set("已停止");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "压缩图片", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally
        {
            finished = true;
            bool stopped = cancellation.IsCancellationRequested;
            cancellation.Dispose();
            cancellation = null;
            Progress.Visibility = Visibility.Collapsed;
            Heading.Text = stopped ? "压缩已停止" : "压缩完成";
            Secondary.Content = "关闭";
            Feedback.Text = string.Join("    ", new[] {
                $"已保存 {files.Count(x => x.Result?.Outcome == Outcome.Written)} 张",
                $"已跳过 {files.Count(x => x.Result?.Outcome == Outcome.Skipped)} 张",
                $"失败 {files.Count(x => x.Result?.Outcome == Outcome.Failed)} 张" });
            if (files.FirstOrDefault(x => x.Result?.Outcome == Outcome.Failed)?.Result is { } failure)
                Feedback.Text += $"    {Path.GetFileName(failure.Source)}：{failure.Note}";
            RefreshControls();
            if (closeWhenStopped) Close();
        }
    }
    private void SecondaryClick(object sender, RoutedEventArgs e)
    {
        if (cancellation is not null)
        {
            cancellation.Cancel();
            Feedback.Text = "正在停止…";
        }
        else Close();
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (cancellation is null || finished) return;
        closeWhenStopped = true;
        cancellation.Cancel();
        e.Cancel = true;
    }
    private void Reveal(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { DataContext: FileItem { Output: { } path } } || !File.Exists(path)) return;
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            start.Arguments = "/select,\"" + path + "\"";
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) { Feedback.Text = ex.Message; }
    }
}
