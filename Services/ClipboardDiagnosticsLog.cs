using System.IO;
using System.Text;

namespace ClipboardManager;

/// <summary>
/// 剪贴板粘贴 / 监控诊断日志，与 Shell 跳转日志同目录：<c>%LocalAppData%\ClipboardX\clipboard_diagnostics.log</c>
/// </summary>
internal static class ClipboardDiagnosticsLog
{
    private static readonly object Gate = new();
    private static readonly int MaxBytesBeforeTrim = 2_000_000;

    public static string LogFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipboardX",
            "clipboard_diagnostics.log");

    public static void Write(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [clipboard] {message}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
                TrimIfHuge();
            }
        }
        catch
        {
            /* 日志不得影响主流程 */
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
