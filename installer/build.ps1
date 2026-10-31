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

# The PawnIO driver (for the RAM over SMBus), official and signed; run by the installer only when missing.
$pawnio = Join-Path $root 'publish\thirdparty\PawnIO_setup.exe'
if (-not (Test-Path $pawnio)) {
    New-Item -ItemType Directory -Force (Split-Path $pawnio) | Out-Null
    Invoke-WebRequest 'https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe' -OutFile $pawnio
}
if ((Get-FileHash $pawnio).Hash -ne '1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032') { Remove-Item $pawnio; throw 'PawnIO_setup.exe does not match the published SHA-256.' }

& $iscc "/DAppVersion=$version" "/DSourceDir=$sc" (Join-Path $PSScriptRoot 'RgbStudio.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed.' }
Get-Item (Join-Path $root "publish\RgbStudio-$version-setup.exe")
