[CmdletBinding()]
param([switch]$StartWithWindows, [switch]$DesktopShortcut, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\ScreenSound-Rainmeter'
$exe = Join-Path $destination 'ScreenSound.exe'
$shortcutName = 'ScreenSound Rainmeter Edition.lnk'
if (-not (Test-Path -LiteralPath (Join-Path $source 'ScreenSound.exe'))) {
    throw 'Extract the entire release ZIP before running Install.cmd.'
}
if ([IO.Path]::GetFullPath($source).TrimEnd('\') -eq [IO.Path]::GetFullPath($destination).TrimEnd('\')) {
    throw 'Already installed here. Start ScreenSound.exe instead.'
}
$active = @(Get-Process -Name ScreenSound -ErrorAction SilentlyContinue)
if ($active.Count -gt 0) {
    throw 'Exit ScreenSound from its tray icon, then run Install.cmd again. Both editions share a single-instance guard.'
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($item in (Get-ChildItem -LiteralPath $source)) {
    if ($item.Name -notin @('settings.json', 'RainmeterBindings.json', 'install-state.json')) {
        Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse -Force
    }
}
$shell = New-Object -ComObject WScript.Shell
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) $shortcutName
$shortcut = $shell.CreateShortcut($startMenu)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $destination
$shortcut.IconLocation = $exe + ',0'
$shortcut.Description = 'ScreenSound with per-monitor Rainmeter synchronization'
$shortcut.Save()
if ($DesktopShortcut) {
    Copy-Item -LiteralPath $startMenu -Destination (Join-Path ([Environment]::GetFolderPath('Desktop')) $shortcutName) -Force
}
$settingsPath = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'ScreenSound\settings.json'
$settings = $null
if (Test-Path -LiteralPath $settingsPath) { $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$previousRun = Get-ItemPropertyValue -LiteralPath $runKey -Name ScreenSound -ErrorAction SilentlyContinue
$manifest = Join-Path $destination 'install-state.json'
if (-not (Test-Path -LiteralPath $manifest)) {
    @{ PreviousStartup = $previousRun } | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding UTF8
}
if ($StartWithWindows -or ($settings -and $settings.AutoStartWithWindows)) {
    if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
    Set-ItemProperty -LiteralPath $runKey -Name ScreenSound -Value ('"' + $exe + '"')
}
if ($StartWithWindows) {
    if (-not $settings) { $settings = [pscustomobject]@{} }
    $settings | Add-Member -NotePropertyName AutoStartWithWindows -NotePropertyValue $true -Force
    New-Item -ItemType Directory -Path (Split-Path -Parent $settingsPath) -Force | Out-Null
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
Write-Output 'Installed. Open ScreenSound Rainmeter Edition from Start, then Settings > Configure visualizers.'
Write-Output 'The original application and existing speaker assignments were preserved.'
if (-not $NoLaunch) { Start-Process -FilePath $exe -WorkingDirectory $destination -WindowStyle Hidden }
