using System.Windows;

namespace ImagePress.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var state = UserState.ForUser();
            string action = "main";
            string[] paths = e.Args;
            if (e.Args.FirstOrDefault() == "--shell")
            {
                if (e.Args.Length != 3 || e.Args[1] is not ("main" or "configure" or "repeat" or "500" or "1000") ||
                    !Guid.TryParseExact(e.Args[2], "N", out var job))
                    throw new ArgumentException("无效的右键菜单请求。");
                action = e.Args[1];
                paths = state.TakeSelection(job);
            }
            var window = new MainWindow(state, action, paths);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "压缩图片", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
