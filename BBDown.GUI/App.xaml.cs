using System;
using System.IO;
using System.Windows;

using Ookii.Dialogs.Wpf;

namespace BBDown.GUI;

public partial class App : Application
{
    public static class Program
    {
        [STAThread]
        public static void Main( )
        {
            App app = new( );
            app.InitializeComponent( );
            // UI 线程未捕获异常会直接崩溃退出：记录日志、提示后正常关闭
            app.DispatcherUnhandledException += (_, e) =>
            {
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "BBDown.GUI.error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\n");
                TaskDialog dialog = new( )
                {
                    WindowTitle = "BBDown.GUI 错误",
                    MainInstruction = "发生未处理的异常，程序即将退出",
                    Content = e.Exception.Message,
                    MainIcon = TaskDialogIcon.Error,
                    Buttons = { new TaskDialogButton(ButtonType.Ok) },
                };
                dialog.ShowDialog( );
                e.Handled = true;
                app.Shutdown( );
            };
            app.Run( );
        }
    }
}
