# Dev-only uninstaller (run as administrator). Removes only Ecoar's own values.
param([string]$Endpoint = '{4d824189-548f-49c1-af0a-eb9154dd1be1}')
$clsid = '{5E0C2A91-3B7D-4C6E-9F18-A2D4B6C8E0F1}'
$dir = 'C:\Program Files\Ecoar'
$fx = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Capture\$Endpoint\FxProperties"
$sfx = '{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},5'
$modes = '{d3993a3f-99c2-4402-b5ec-a92a0367664b},5'
$v = (Get-ItemProperty $fx -Name $sfx -ErrorAction SilentlyContinue).$sfx
if ($v -eq $clsid) {
  Remove-ItemProperty $fx -Name $sfx; "removed SFX"
  $m = (Get-ItemProperty $fx -Name $modes -ErrorAction SilentlyContinue).$modes
  if (($m -join ',') -in '{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3}', '{C18E2F7E-933D-4965-B7D1-1EEF228D2AF3},{9E90EA20-B493-4FD1-A1A8-7E1361A956CF}') { Remove-ItemProperty $fx -Name $modes; "removed modes" }
}
# Mode effect (MFX, slot 6) used for RAW captures.
$mfx = '{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6'; $mfxModes = '{d3993a3f-99c2-4402-b5ec-a92a0367664b},6'
if ((Get-ItemProperty $fx -Name $mfx -ErrorAction SilentlyContinue).$mfx -eq $clsid) {
  Remove-ItemProperty $fx -Name $mfx; "removed MFX"
  if (((Get-ItemProperty $fx -Name $mfxModes -ErrorAction SilentlyContinue).$mfxModes -join ',') -eq '{9E90EA20-B493-4FD1-A1A8-7E1361A956CF}') { Remove-ItemProperty $fx -Name $mfxModes; "removed MFX modes" }
}
Stop-Service audiosrv -Force
if (Test-Path "$dir\EcoarApo.dll") { Start-Process regsvr32.exe -ArgumentList '/u', '/s', "`"$dir\EcoarApo.dll`"" -Wait }
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
Start-Service audiosrv
Remove-Item 'C:\ProgramData\Ecoar' -Recurse -Force -ErrorAction SilentlyContinue
"uninstalled"
