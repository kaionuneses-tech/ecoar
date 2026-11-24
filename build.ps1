# Builds the native parts of Ecoar into ecoar\out with any Visual Studio 2022 C++ toolset
# (Build Tools locally, Enterprise on GitHub's runners).
# No $ErrorActionPreference='Stop' around cmd.exe: vcvars prints a harmless 'vswhere' line on stderr.
$here = $PSScriptRoot
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsRoot = if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
$vcvars = if ($vsRoot) { Join-Path $vsRoot 'VC\Auxiliary\Build\vcvars64.bat' }
if (-not $vcvars -or -not (Test-Path $vcvars)) { throw "vcvars64.bat not found: install Visual Studio 2022 Build Tools with the C++ workload" }
$out = Join-Path $here 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$apo = Join-Path $here 'apo'
$cl = 'cl /nologo /O2 /EHsc /W4 /MT'
$tools = Join-Path $here 'tools'
$targets = @(
  "$cl `"$apo\tests\apo_tests.cpp`" `"$apo\RingReader.cpp`" `"$apo\Mixer.cpp`" /Fe:apo_tests.exe",
  "$cl /LD `"$apo\EcoarApo.cpp`" `"$apo\RingReader.cpp`" `"$apo\Mixer.cpp`" `"$apo\SharedRing.cpp`" `"$apo\Log.cpp`" /Fe:EcoarApo.dll /link /DEF:`"$apo\EcoarApo.def`" ole32.lib advapi32.lib",
  "$cl `"$tools\ringprobe.cpp`" /Fe:ringprobe.exe",
  "$cl `"$tools\captest.cpp`" /Fe:captest.exe /link ole32.lib"
)
$cmd = "`"$vcvars`" >nul 2>nul && cd /d `"$out`" && " + ($targets -join ' && ')
cmd.exe /c $cmd
if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
