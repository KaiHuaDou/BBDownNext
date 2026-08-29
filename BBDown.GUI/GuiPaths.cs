using System;
using System.IO;

namespace BBDown.GUI;

/// <summary>GUI 持久化文件目录；随 exe 存放于同目录（portable）。</summary>
public static class GuiPaths
{
    public static string ExeDirectory( )
    {
        return Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    }
}
