#pragma warning disable CA2000 // lifetime 由 Avalonia 应用生命周期管理，随进程退出释放

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BBDown.GUI;

public partial class App : Application
{
    public override void Initialize( )
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted( )
    {
        // 手动设置 MainWindow（而非 StartWithClassicDesktopLifetime 的反射查找），AOT 下类型引用显式
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow( );
        }

        base.OnFrameworkInitializationCompleted( );
    }

    public static AppBuilder BuildAvaloniaApp( )
    {
        return AppBuilder.Configure<App>( ).UsePlatformDetect( ).WithInterFont( );
    }
}
