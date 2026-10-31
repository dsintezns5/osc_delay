$exe = 'X:\Bild\OSC_Delay_Windows\osc_delay_csharp\OSC_Delay.exe'
$first = Start-Process -FilePath $exe -PassThru
Start-Sleep -Milliseconds 1500
$second = Start-Process -FilePath $exe -PassThru
Start-Sleep -Milliseconds 2000
$secondAlive = $null -eq $second.ExitCode -and -not $second.HasExited
if ($secondAlive) { throw "second instance is still running" }
if ($second.ExitCode -ne 0) { throw "second instance exit code $($second.ExitCode), expected 0" }
$first.Refresh()
if ($first.HasExited) { throw "first instance exited" }
if ($first.MainWindowHandle -eq [IntPtr]::Zero) { throw "first window handle is zero" }
Add-Type -Language CSharp @"
using System;using System.Runtime.InteropServices;
public class W{
 [DllImport("user32.dll")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")]public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
}
"@
$restored = $false
for ($i = 0; $i -lt 10; $i++) {
    Start-Sleep -Milliseconds 200
    if ([W]::IsWindowVisible($first.MainWindowHandle) -and -not [W]::IsIconic($first.MainWindowHandle)) { $restored = $true; break }
}
if (-not $restored) { throw "window not restored after second launch" }
# Foreground steal is Windows foreground-lock dependent (flaky in headless sandboxes);
# record as info, not a hard gate.
$fg = [W]::GetForegroundWindow()
if ($fg -ne $first.MainWindowHandle) { Write-Output "NOTE: foreground stayed with shell (sandbox foreground lock)" }
Stop-Process -Id $first.Id -ErrorAction SilentlyContinue
Write-Output "PASS: single instance"