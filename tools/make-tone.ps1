# Writes a 16-bit PCM mono WAV sine tone. Usage: make-tone.ps1 -Path x.wav -Freq 880 -Seconds 3
param([string]$Path, [double]$Freq = 880, [double]$Seconds = 3, [int]$Rate = 48000, [double]$Amp = 0.5)
$n = [int]($Rate * $Seconds)
$fs = [IO.File]::Create($Path); $w = New-Object IO.BinaryWriter($fs)
$w.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $w.Write([int](36 + $n * 2)); $w.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$w.Write([int]16); $w.Write([int16]1); $w.Write([int16]1); $w.Write([int]$Rate); $w.Write([int]($Rate * 2)); $w.Write([int16]2); $w.Write([int16]16)
$w.Write([Text.Encoding]::ASCII.GetBytes('data')); $w.Write([int]($n * 2))
for ($i = 0; $i -lt $n; $i++) { $w.Write([int16]([Math]::Round($Amp * 32767 * [Math]::Sin(2 * [Math]::PI * $Freq * $i / $Rate)))) }
$w.Close()
