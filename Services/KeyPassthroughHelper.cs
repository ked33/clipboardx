namespace ClipboardManager;

internal static class KeyPassthroughHelper
{
    public static string FormatRule(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & Win32.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Win32.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Win32.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & Win32.MOD_WIN) != 0) parts.Add("Win");
        if ((modifiers & Win32.MOD_CAPS) != 0) parts.Add("CapsLock");
        if (key == 0)
            parts.Add("*");
        else
            parts.Add(AppSettings.FormatSingleVk(key));
        return string.Join("+", parts);
    }

    public static bool IsEssentialPanelKey(uint vk) => vk switch
    {
        Win32.VK_ESCAPE or Win32.VK_RETURN or Win32.VK_BACK or Win32.VK_DELETE
            or Win32.VK_UP or Win32.VK_DOWN or Win32.VK_LEFT or Win32.VK_RIGHT
            or Win32.VK_HOME or Win32.VK_END or Win32.VK_PRIOR or Win32.VK_NEXT => true,
        _ => false
    };

    public static bool IsModifierVk(uint vk) => vk is 0x10 or 0x11 or 0x14
        or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    /// <summary>将物理修饰键 VK 映射为穿透锁存位（与 <see cref="Win32.MOD_*"/> / MOD_CAPS 一致）。</summary>
    public static uint VkToModifierLatchBit(uint vk) => vk switch
    {
        Win32.VK_CAPITAL => Win32.MOD_CAPS,
        0x10 or 0xA0 or 0xA1 => Win32.MOD_SHIFT,
        0x11 or 0xA2 or 0xA3 => Win32.MOD_CONTROL,
        0x12 or 0xA4 or 0xA5 => Win32.MOD_ALT,
        0x5B or 0x5C => Win32.MOD_WIN,
        _ => 0
    };

    private static bool IsCtrlPhysicallyDown(uint modifierLatch) =>
        (modifierLatch & Win32.MOD_CONTROL) != 0
        || (Win32.GetAsyncKeyState(0x11) & 0x8000) != 0
        || (Win32.GetAsyncKeyState(0xA2) & 0x8000) != 0
        || (Win32.GetAsyncKeyState(0xA3) & 0x8000) != 0;

    private static bool IsShiftPhysicallyDown(uint modifierLatch) =>
        (modifierLatch & Win32.MOD_SHIFT) != 0
        || (Win32.GetAsyncKeyState(0x10) & 0x8000) != 0
        || (Win32.GetAsyncKeyState(0xA0) & 0x8000) != 0
        || (Win32.GetAsyncKeyState(0xA1) & 0x8000) != 0;

    private static bool IsAltPhysicallyDown(uint modifierLatch) =>
        (modifierLatch & Win32.MOD_ALT) != 0
        || (Win32.GetAsyncKeyState(0x12) & 0x8000) != 0
        || (Win32.GetAsyncKeyState(0xA4) & 0x8000) != 0
        || (Win32.GetAsyncKeyState(0xA5) & 0x8000) != 0;

    private static bool IsWinPhysicallyDown(uint modifierLatch) =>
        (modifierLatch & Win32.MOD_WIN) != 0
        || (Win32.GetAsyncKeyState(0x5B) & 0x8000) != 0
        || (Win32.GetAsyncKeyState(0x5C) & 0x8000) != 0;

    /// <summary>Caps 用锁存或物理按下（高 bit）；不用 toggle 低 bit，避免 Caps 灯亮时误穿透。</summary>
    private static bool IsCapsPhysicallyDown(uint modifierLatch) =>
        (modifierLatch & Win32.MOD_CAPS) != 0
        || (Win32.GetAsyncKeyState((int)Win32.VK_CAPITAL) & 0x8000) != 0;

    public static bool ModifierMaskMatches(uint requiredMods, uint modifierLatch)
    {
        bool reqCtrl = (requiredMods & Win32.MOD_CONTROL) != 0;
        bool reqShift = (requiredMods & Win32.MOD_SHIFT) != 0;
        bool reqAlt = (requiredMods & Win32.MOD_ALT) != 0;
        bool reqWin = (requiredMods & Win32.MOD_WIN) != 0;
        bool reqCaps = (requiredMods & Win32.MOD_CAPS) != 0;
        return IsCtrlPhysicallyDown(modifierLatch) == reqCtrl
            && IsShiftPhysicallyDown(modifierLatch) == reqShift
            && IsAltPhysicallyDown(modifierLatch) == reqAlt
            && IsWinPhysicallyDown(modifierLatch) == reqWin
            && IsCapsPhysicallyDown(modifierLatch) == reqCaps;
    }

    public static bool AnyConfiguredModifierHeld(uint mask, uint modifierLatch)
    {
        if (mask == 0) return false;
        if ((mask & Win32.MOD_CONTROL) != 0 && IsCtrlPhysicallyDown(modifierLatch))
            return true;
        if ((mask & Win32.MOD_SHIFT) != 0 && IsShiftPhysicallyDown(modifierLatch))
            return true;
        if ((mask & Win32.MOD_ALT) != 0 && IsAltPhysicallyDown(modifierLatch))
            return true;
        if ((mask & Win32.MOD_WIN) != 0 && IsWinPhysicallyDown(modifierLatch))
            return true;
        if ((mask & Win32.MOD_CAPS) != 0 && IsCapsPhysicallyDown(modifierLatch))
            return true;
        return false;
    }

    /// <summary>仅根据 GetAsyncKeyState 判断某修饰键族是否仍按住（用于 KeyUp 后校正锁存）。</summary>
    public static bool IsModifierFamilyPhysicallyDown(uint modBit) => modBit switch
    {
        Win32.MOD_CONTROL => (Win32.GetAsyncKeyState(0x11) & 0x8000) != 0
            || (Win32.GetAsyncKeyState(0xA2) & 0x8000) != 0
            || (Win32.GetAsyncKeyState(0xA3) & 0x8000) != 0,
        Win32.MOD_SHIFT => (Win32.GetAsyncKeyState(0x10) & 0x8000) != 0
            || (Win32.GetAsyncKeyState(0xA0) & 0x8000) != 0
            || (Win32.GetAsyncKeyState(0xA1) & 0x8000) != 0,
        Win32.MOD_ALT => (Win32.GetAsyncKeyState(0x12) & 0x8000) != 0
            || (Win32.GetAsyncKeyState(0xA4) & 0x8000) != 0
            || (Win32.GetAsyncKeyState(0xA5) & 0x8000) != 0,
        Win32.MOD_WIN => (Win32.GetAsyncKeyState(0x5B) & 0x8000) != 0
            || (Win32.GetAsyncKeyState(0x5C) & 0x8000) != 0,
        Win32.MOD_CAPS => (Win32.GetAsyncKeyState((int)Win32.VK_CAPITAL) & 0x8000) != 0,
        _ => false
    };

    public static bool ShouldPassthrough(
        AppSettings? settings,
        uint vk,
        bool isKeyDown,
        uint modifierLatch)
    {
        if (settings is not { KeyPassthroughEnabled: true })
            return false;
        if (!isKeyDown)
            return false;
        if (IsModifierVk(vk))
            return false;

        if (settings.KeyPassthroughKeepPanelKeys && IsEssentialPanelKey(vk))
            return false;

        var rules = settings.KeyPassthroughRules;
        if (rules is { Count: > 0 })
        {
            foreach (var rule in rules)
            {
                if (!ModifierMaskMatches(rule.Modifiers, modifierLatch))
                    continue;
                if (rule.Key == 0 || rule.Key == vk)
                    return true;
            }
        }

        if (AnyConfiguredModifierHeld(settings.KeyPassthroughModifierMask, modifierLatch))
            return true;

        return false;
    }
}
