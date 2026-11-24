# Dev-only installer (run as administrator). Replaced by `Ecoar.exe --setup` in Task 8.
param([string]$Endpoint = '{4d824189-548f-49c1-af0a-eb9154dd1be1}')   # Fuxi-H3 "Microfone"
$ErrorActionPreference = 'Stop'
$clsid = '{5E0C2A91-3B7D-4C6E-9F18-A2D4B6C8E0F1}'
$dir = 'C:\Program Files\Ecoar'
$data = 'C:\ProgramData\Ecoar'
$fx = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\$Endpoint\FxProperties"
$sfx = '{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5'
$modes = '{d3993a3f-99c2-4402-b5ec-a92a0367664b},5'
if (-not (Test-Path $fx)) { throw "endpoint FxProperties not found: $fx" }

$cur = (Get-ItemProperty $fx -Name $sfx -ErrorAction SilentlyContinue).$sfx
if ($cur -and $cur -ne $clsid) { throw "SFX already used by $cur; not overwriting" }

New-Item -ItemType Directory -Force $data | Out-Null
foreach ($sid in '*S-1-5-19', '*S-1-5-12', '*S-1-15-2-1', '*S-1-5-4') { icacls $data /grant "${sid}:(OI)(CI)M" | Out-Null }

# The DLL may be locked by audiodg when updating.
Stop-Service audiosrv -Force
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item (Join-Path $PSScriptRoot '..\out\EcoarApo.dll') $dir -Force
$p = Start-Process regsvr32.exe -ArgumentList '/s', "`"$dir\EcoarApo.dll`"" -Wait -PassThru
if ($p.ExitCode -ne 0) { Start-Service audiosrv; throw "regsvr32 failed ($($p.ExitCode))" }

New-ItemProperty $fx -Name $sfx -Value $clsid -PropertyType String -Force | Out-Null
if (-not (Get-ItemProperty $fx -Name $modes -ErrorAction SilentlyContinue)) {
  # DEFAULT + RAW: voice apps (Discord) capture in raw mode, which skips DEFAULT-only SFXs.
  New-ItemProperty $fx -Name $modes -Value @('{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}', '{9E90EA20-B493-4FD1-A1A8-7E1361A956CF}') -PropertyType MultiString | Out-Null
}
Start-Service audiosrv
"installed on $Endpoint"
