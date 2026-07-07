# 剪贴板占用实时监控（排查 CLIPBRD_E_CANT_OPEN 用）
# 用法: powershell -ExecutionPolicy Bypass -File scripts/clip-holder-watch.ps1
# Ctrl+C 结束；输出写入同目录 clip-holder-watch.log

$ErrorActionPreference = 'SilentlyContinue'
$logPath = Join-Path $PSScriptRoot 'clip-holder-watch.log'
$sig = @'
using System;
using System.Runtime.InteropServices;
public static class ClipHolderWatch {
  [DllImport("user32.dll")] public static extern IntPtr GetOpenClipboardWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@
Add-Type -TypeDefinition $sig -ErrorAction Stop

"=== clip-holder-watch started {0:yyyy-MM-dd HH:mm:ss} ===" -f (Get-Date) | Tee-Object -FilePath $logPath -Append
Write-Host "监控中… 日志: $logPath （Ctrl+C 结束）"

$last = ''
while ($true) {
    $h = [ClipHolderWatch]::GetOpenClipboardWindow()
    if ($h -ne [IntPtr]::Zero) {
        [uint32]$pid = 0
        [void][ClipHolderWatch]::GetWindowThreadProcessId($h, [ref]$pid)
        $p = Get-Process -Id $pid -ErrorAction SilentlyContinue
        $name = if ($p) { $p.ProcessName } else { '?' }
        $path = if ($p -and $p.Path) { $p.Path } else { '' }
        $line = "{0:HH:mm:ss.fff} HOLD hwnd=0x{1:X} pid={2} name={3} path={4}" -f (Get-Date), $h.ToInt64(), $pid, $name, $path
        if ($line -ne $last) {
            $line | Tee-Object -FilePath $logPath -Append
            $last = $line
        }
    }
    else {
        $last = ''
    }
    Start-Sleep -Milliseconds 20
}
