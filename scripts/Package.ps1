[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+-rainmeter\.\d+$')][string]$Version = '2.1.0-rainmeter.1',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts'),
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
$project = Join-Path $root 'ScreenSound\ScreenSound.csproj'
New-Item -ItemType Directory -Path $output -Force | Out-Null
foreach ($selfContained in @($false, $true)) {
    $flavor = if ($selfContained) { 'self-contained' } else { 'framework-dependent' }
    $staging = Join-Path $output ('staging-' + $flavor + '-' + [Guid]::NewGuid().ToString('N'))
    & $Dotnet publish $project -c Release -r win-x64 --self-contained $selfContained.ToString().ToLowerInvariant() `
        -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=false `
        -p:RestoreSources=https://api.nuget.org/v3/index.json -o $staging
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $flavor" }
    foreach ($file in @('LICENSE', 'README.md', 'CHANGELOG.md', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $staging
    }
    Copy-Item -LiteralPath (Join-Path $root 'docs'), (Join-Path $root 'licenses') -Destination $staging -Recurse
    Get-ChildItem -LiteralPath (Join-Path $root 'installer') -File | Where-Object Extension -in '.ps1', '.cmd' |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $staging }
    if ($selfContained) {
        $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages' }
        foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
            $runtimeRoot = Join-Path $packageRoot $runtime
            if (Test-Path -LiteralPath $runtimeRoot) {
                foreach ($notice in (Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse | Where-Object Name -match '^(LICENSE|THIRD.PARTY.NOTICES)')) {
                    Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $staging ("licenses\$runtime-$($notice.Directory.Name)-$($notice.Name)"))
                }
            }
        }
    }
    $unexpected = Get-ChildItem -LiteralPath $staging -File -Recurse | Where-Object {
        $_.Name -in @('settings.json', 'RainmeterBindings.json', 'install-state.json') -or $_.Name -like '*.screensound.bak'
    }
    if ($unexpected) { throw 'Runtime settings or personal skin backups found in release staging.' }
    $zip = Join-Path $output "ScreenSound-Rainmeter-$Version-win-x64-$flavor.zip"
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
    Write-Output $zip
}
$checksums = Get-ChildItem -LiteralPath $output -Filter "ScreenSound-Rainmeter-$Version-win-x64-*.zip" | Sort-Object Name | ForEach-Object {
    (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
