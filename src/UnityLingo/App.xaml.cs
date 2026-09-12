using System.Windows;

namespace UnityLingo;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { MainWindow = new MainWindow(); MainWindow.Show(); }
        catch
        {
            MessageBox.Show("无法加载本地设置或历史数据库。请检查磁盘和目录权限，或备份后修复 %LOCALAPPDATA%\\UnityLingo 中的数据文件。原文件不会被自动覆盖。", "UnityLingo 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
