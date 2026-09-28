# Exercise the real installer/uninstaller only in an isolated CI runner profile.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This smoke test requires a disposable GitHub Actions runner.' }
$destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\ScreenSound-Rainmeter'
if (Test-Path -LiteralPath $destination) { throw 'Expected an empty install destination.' }
$settings = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'ScreenSound\settings.json'
if (Test-Path -LiteralPath $settings) { throw 'Expected an empty ScreenSound profile.' }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runBefore = Get-ItemPropertyValue -LiteralPath $runKey -Name ScreenSound -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path (Split-Path -Parent $settings) -Force | Out-Null
'{"Mappings":[],"AutoStartWithWindows":false,"TestSentinel":"keep-me"}' | Set-Content -LiteralPath $settings -Encoding UTF8
& (Join-Path $PackageDirectory 'Install.ps1') -NoLaunch -StartWithWindows
$exe = Join-Path $destination 'ScreenSound.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Application was not installed.' }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'ScreenSound Rainmeter Edition.lnk'
$shell = New-Object -ComObject WScript.Shell
if ($shell.CreateShortcut($shortcut).TargetPath -ne $exe) { throw 'Incorrect shortcut target.' }
if ((Get-ItemPropertyValue -LiteralPath $runKey -Name ScreenSound) -ne ('"' + $exe + '"')) { throw 'Startup points at another edition.' }
$saved = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
if ($saved.TestSentinel -ne 'keep-me' -or -not $saved.AutoStartWithWindows) { throw 'Settings were not preserved correctly.' }
& (Join-Path $destination 'Uninstall.ps1')
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $shortcut)) { throw 'Uninstall left the application or shortcut behind.' }
if ((Get-ItemPropertyValue -LiteralPath $runKey -Name ScreenSound -ErrorAction SilentlyContinue) -ne $runBefore) { throw 'Previous startup entry was not restored.' }
if ((Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).TestSentinel -ne 'keep-me') { throw 'Uninstall removed shared settings.' }
Write-Output 'PASS real per-user install, shortcut, startup selection, settings preservation, and uninstall.'
