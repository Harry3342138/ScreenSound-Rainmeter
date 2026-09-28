$ErrorActionPreference = 'Stop'
$expected = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\ScreenSound-Rainmeter')).TrimEnd('\')
$actual = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
if ($actual -ne $expected) { throw 'Run this from the installed ScreenSound-Rainmeter folder.' }
if ((Get-Item -LiteralPath $actual).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to remove a redirected install folder.' }
$exe = Join-Path $actual 'ScreenSound.exe'
foreach ($process in @(Get-Process ScreenSound -ErrorAction SilentlyContinue)) {
    if ($process.Path -eq $exe) { throw 'First restore skins in Settings if desired, then exit ScreenSound from the tray and retry.' }
}
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$entry = (Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).ScreenSound
if ($entry -eq ('"' + $exe + '"')) {
    $statePath = Join-Path $actual 'install-state.json'
    $previous = if (Test-Path -LiteralPath $statePath) { (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).PreviousStartup } else { $null }
    if ($previous) { Set-ItemProperty -LiteralPath $runKey -Name ScreenSound -Value $previous }
    else { Remove-ItemProperty -LiteralPath $runKey -Name ScreenSound }
}
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
    $shortcut = Join-Path $folder 'ScreenSound Rainmeter Edition.lnk'
    if ((Test-Path -LiteralPath $shortcut) -and $shell.CreateShortcut($shortcut).TargetPath -eq $exe) { Remove-Item -LiteralPath $shortcut }
}
# Only this exact per-user installation folder is removed. Shared settings and skins remain.
Remove-Item -LiteralPath $actual -Recurse -Force
Write-Output 'Uninstalled Rainmeter Edition. Speaker settings, skin files, and skin backups were kept.'
