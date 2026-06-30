namespace ClipboardManager;

/// <summary>面板打开时交给外部钩子（如 AutoHotkey）处理的组合键；Key 为 0 表示修饰键按住时任意键穿透。</summary>
public class KeyPassthroughRule
{
    public uint Modifiers { get; set; }
    public uint Key { get; set; }

    public string DisplayName => KeyPassthroughHelper.FormatRule(Modifiers, Key);
}
