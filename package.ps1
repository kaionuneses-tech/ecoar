# Builds, tests and packages Ecoar into dist\Ecoar-amigos.zip (used locally and by the GitHub release workflow).
# -SkipDesktopTests leaves out tests that need an audio device, Media Foundation or a desktop (CI runners).
param([switch]$SkipDesktopTests)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$pub = Join-Path $dist 'Ecoar'

& (Join-Path $root 'build.ps1')
& (Join-Path $root 'out\apo_tests.exe')
if ($LASTEXITCODE -ne 0) { throw "native tests failed" }

$filter = if ($SkipDesktopTests) { @('--filter', 'Category!=Desktop') } else { @() }
dotnet test (Join-Path $root 'Ecoar.sln') -c Release @filter
if ($LASTEXITCODE -ne 0) { throw "dotnet tests failed" }

if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish (Join-Path $root 'app\Ecoar.csproj') -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $pub
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
if (-not (Test-Path (Join-Path $pub 'EcoarApo.dll'))) { throw "EcoarApo.dll missing from the package" }

Copy-Item (Join-Path $root 'package\*') $pub
$zip = Join-Path $dist 'Ecoar-amigos.zip'
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $pub '*') -DestinationPath $zip
"package: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
