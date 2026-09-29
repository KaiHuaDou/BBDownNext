using System;
using System.IO;

namespace BBDown.Core.Tests;

/// <summary>用例独享的临时目录，释放时递归删除。</summary>
internal sealed class TempDir : IDisposable
{
    public string FullPath { get; } = Path.Combine(Path.GetTempPath( ), "bbdown_test_" + Path.GetRandomFileName( ));

    public TempDir( )
    {
        Directory.CreateDirectory(FullPath);
    }

    public void Dispose( )
    {
        try
        {
            Directory.Delete(FullPath, true);
        }
        catch (IOException)
        {
            // 上一个用例的句柄尚未释放时删除会失败；目录在系统临时目录下，不清理不影响断言
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
