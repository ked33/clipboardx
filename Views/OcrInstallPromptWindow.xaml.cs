using System.Windows;

namespace ClipboardManager;

public enum OcrInstallPromptResult
{
    Later,
    InstallViaSettings,
    InstallElevated
}

public partial class OcrInstallPromptWindow : Window
{
    public OcrInstallPromptResult Result { get; private set; } = OcrInstallPromptResult.Later;

    private readonly string _languageTag;

    private OcrInstallPromptWindow(string languageTag)
    {
        _languageTag = languageTag;
        InitializeComponent();
        var display = OcrLanguageInstaller.GetLanguageDisplayName(languageTag);
        BodyText.Text =
            $"识别图片中的文字需要 Windows 的「{display}」OCR 组件（约几十 MB，从 Windows 更新下载）。\n\n" +
            "推荐：点击「打开系统设置」，在语言选项中勾选「光学字符识别」后安装。";
    }

    internal static OcrInstallPromptResult ShowDialog(Window? owner, string languageTag)
    {
        var w = new OcrInstallPromptWindow(languageTag);
        if (owner != null && owner.IsLoaded)
            w.Owner = owner;
        w.ShowDialog();
        return w.Result;
    }

    private void LaterBtn_Click(object sender, RoutedEventArgs e)
    {
        Result = OcrInstallPromptResult.Later;
        Close();
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        OcrLanguageInstaller.OpenLanguageSettings();
        Result = OcrInstallPromptResult.InstallViaSettings;
        Close();
    }

    private void ElevatedBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!OcrLanguageInstaller.TryStartElevatedInstall(_languageTag))
        {
            System.Windows.MessageBox.Show(this,
                "无法启动安装程序。请尝试「打开系统设置」手动安装，或以管理员身份运行 ClipboardX。",
                "图片文字识别", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Result = OcrInstallPromptResult.InstallElevated;
        Close();
    }
}
