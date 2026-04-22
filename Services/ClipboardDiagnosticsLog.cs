using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;

namespace ClipboardManager;

/// <summary>
/// 剪贴板粘贴 / 监控诊断日志，与 Shell 跳转日志同目录：<c>%LocalAppData%\ClipboardX\clipboard_diagnostics.log</c>
///
/// 写入是生产者无锁入队 + 后台单线程批量 flush。
/// 早期版本在 UI 线程上同步 <c>File.AppendAllText</c>，一次粘贴的多条日志会叠加成可感知卡顿（尤其首次落盘 / 杀软扫描时），
/// 现在所有 <see cref="Write"/> 调用对 UI 线程都是 O(1) 入队，磁盘 IO 全部在后台线程发生。
/// </summary>
internal static class ClipboardDiagnosticsLog
{
    private static readonly ConcurrentQueue<string> Queue = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static readonly int MaxBytesBeforeTrim = 2_000_000;
    private static int _started;

    public static string LogFilePath => AppPaths.ClipboardDiagnosticsLogFile;

    public static void Write(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [clipboard] {message}{Environment.NewLine}";
            Queue.Enqueue(line);
            EnsureWriterStarted();
            Signal.Set();
        }
        catch
        {
            /* 日志不得影响主流程 */
        }
    }

    private static void EnsureWriterStarted()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return;
        var t = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "ClipboardDiagnosticsLogWriter",
            Priority = ThreadPriority.BelowNormal
        };
        t.Start();
    }

    private static void WriterLoop()
    {
        var sb = new StringBuilder(4096);
        while (true)
        {
            Signal.WaitOne();
            // 合并队列里所有积压条目，单次写盘减少 IO 次数。
            sb.Clear();
            while (Queue.TryDequeue(out var line))
                sb.Append(line);
            if (sb.Length == 0) continue;

            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                TrimIfHuge();
            }
            catch
            {
                /* ignore */
            }
        }
    }

    private static void TrimIfHuge()
    {
        try
        {
            var fi = new FileInfo(LogFilePath);
            if (!fi.Exists || fi.Length <= MaxBytesBeforeTrim) return;
            using var fs = new FileStream(LogFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var keep = (int)Math.Min(MaxBytesBeforeTrim / 2, fi.Length);
            var buf = new byte[keep];
            fs.Seek(-keep, SeekOrigin.End);
            fs.ReadExactly(buf, 0, keep);
            fs.SetLength(0);
            fs.Write(buf, 0, keep);
        }
        catch
        {
            /* ignore */
        }
    }
}
