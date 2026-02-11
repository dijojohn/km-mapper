using System.Windows;

namespace InputMonitorMapper;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        // Release mouse clip when app exits
        MonitorMapper.ReleaseMouseClip();
        base.OnExit(e);
    }
}
