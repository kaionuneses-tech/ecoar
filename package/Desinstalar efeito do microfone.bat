@echo off
echo Removendo o efeito do Ecoar do microfone...
"%~dp0Ecoar.exe" --uninstall
if %errorlevel%==0 (echo Pronto.) else (echo Algo deu errado ^(codigo %errorlevel%^).)
pause
