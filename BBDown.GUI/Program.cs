using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace BBDown.GUI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ClassicDesktopStyleApplicationLifetime lifetime = new( )
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnLastWindowClose,
        };
        // UI 线程未捕获异常会直接崩溃退出：记录日志、提示后正常关闭
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "BBDown.GUI.error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n");
            ShowErrorDialog(e.Exception.Message, lifetime);
            e.Handled = true;
        };
        App.BuildAvaloniaApp( ).SetupWithLifetime(lifetime);
        lifetime.Start(args);
    }

    private static void ShowErrorDialog(string message, ClassicDesktopStyleApplicationLifetime lifetime)
    {
        Button okButton = new( ) { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right };
        Window dialog = new( )
        {
            Title = "BBDown.GUI 错误",
            Width = 480,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock { Text = "发生未处理的异常，程序即将退出", FontWeight = FontWeight.Bold },
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 10, 0, 0),
                    },
                    okButton,
                },
            },
        };
        okButton.Click += (_, _) =>
        {
            dialog.Close( );
            lifetime.TryShutdown( );
        };
        if (lifetime.MainWindow is { } mainWindow)
        {
            dialog.ShowDialog(mainWindow);
        }
        else
        {
            dialog.Show( );
        }
    }
}
