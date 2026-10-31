# Builds publish\RgbStudio-<version>-setup.exe: tests, a self-contained single-file
# publish, then the Inno Setup installer. Needs the .NET 10 SDK and Inno Setup 6.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'RgbStudio\RgbStudio.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found: winget install JRSoftware.InnoSetup' }

dotnet test (Join-Path $root 'RgbStudio.Tests') -c Release
if ($LASTEXITCODE) { throw 'Tests failed.' }

$sc = Join-Path $root 'publish\sc'
dotnet publish (Join-Path $root 'RgbStudio\RgbStudio.csproj') -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $sc
if ($LASTEXITCODE) { throw 'Publish failed.' }

& $iscc "/DAppVersion=$version" "/DSourceDir=$sc" (Join-Path $PSScriptRoot 'RgbStudio.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed.' }
Get-Item (Join-Path $root "publish\RgbStudio-$version-setup.exe")
