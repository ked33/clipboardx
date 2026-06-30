using System.Diagnostics;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace ClipboardManager;

/// <summary>检测与引导安装 Windows 内置 OCR 语言包（Language.OCR FoD）。</summary>
internal static class OcrLanguageInstaller
{
    public static readonly string[] PreferredLanguageTags = ["zh-CN", "en-US"];

    public static IReadOnlyList<string> GetInstalledLanguageTags()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(l => l.LanguageTag)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static bool IsLanguageInstalled(string languageTag) =>
        GetInstalledLanguageTags().Any(t => string.Equals(t, languageTag, StringComparison.OrdinalIgnoreCase));

    public static bool IsLanguageSupportedByWindows(string languageTag)
    {
        try
        {
            return OcrEngine.IsLanguageSupported(new Language(languageTag));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>返回当前可用于识别的语言标签；无可用引擎时返回 null。</summary>
    public static string? ResolveBestInstalledLanguageTag()
    {
        if (TryCreateEngine(out var engine) && engine != null)
        {
            try { return engine.RecognizerLanguage.LanguageTag; }
            finally { engine = null; }
        }
        return null;
    }

    public static bool TryCreateEngine(out OcrEngine? engine)
    {
        engine = null;
        try
        {
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine != null) return true;

            foreach (var tag in PreferredLanguageTags)
            {
                if (!IsLanguageInstalled(tag)) continue;
                engine = OcrEngine.TryCreateFromLanguage(new Language(tag));
                if (engine != null) return true;
            }

            foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
            {
                engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine != null) return true;
            }
        }
        catch
        {
            engine = null;
        }
        return engine != null;
    }

    /// <summary>首个「系统支持但未安装」的优先 OCR 语言；都已安装则返回 null。</summary>
    public static string? GetFirstMissingPreferredLanguage()
    {
        foreach (var tag in PreferredLanguageTags)
        {
            if (!IsLanguageSupportedByWindows(tag)) continue;
            if (!IsLanguageInstalled(tag)) return tag;
        }
        return null;
    }

    public static void OpenLanguageSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:regionlanguage")
            {
                UseShellExecute = true
            });
        }
        catch { /* ignore */ }
    }

    /// <summary>提权运行 PowerShell 安装 OCR 语言包；用户需在 UAC 点「是」。</summary>
    public static bool TryStartElevatedInstall(string languageTag)
    {
        if (string.IsNullOrWhiteSpace(languageTag)) return false;
        var safeTag = languageTag.Replace("'", "''", StringComparison.Ordinal);
        var script =
            "$cap = Get-WindowsCapability -Online | Where-Object { $_.Name -Like 'Language.OCR*" +
            safeTag + "*' }; " +
            "if ($cap -and $cap.State -eq 'NotPresent') { $cap | Add-WindowsCapability -Online }";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" + script + "\"",
                Verb = "runas",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> WaitForLanguageInstalledAsync(
        string languageTag,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLanguageInstalled(languageTag) && TryCreateEngine(out _))
                return true;
            await Task.Delay(2500, cancellationToken).ConfigureAwait(false);
        }
        return IsLanguageInstalled(languageTag) && TryCreateEngine(out _);
    }

    public static string GetLanguageDisplayName(string languageTag)
    {
        try
        {
            return new Language(languageTag).DisplayName;
        }
        catch
        {
            return languageTag;
        }
    }
}
