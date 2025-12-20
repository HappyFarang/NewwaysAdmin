@echo off
echo Nuking MAUI cache...
for /d /r . %%d in (bin,obj) do @if exist "%%d" rd /s /q "%%d"
echo Done! Reopen VS and rebuild.
pause