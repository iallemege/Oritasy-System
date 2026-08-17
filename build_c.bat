@echo off
setlocal EnableExtensions
rem Standard Edition C (unpacked, EN/ZH): Oritasy_0.0.9.193C.dll
rem Requires Nuclear Option + BepInEx on this machine (paths in tools\write_csc_rsp.ps1).
set ROOT=%~dp0
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%ROOT%"
call "%ROOT%gen_combined_sources.bat"
if errorlevel 1 exit /b 1
if not exist "%ROOT%obj" mkdir "%ROOT%obj"
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\write_csc_rsp.ps1" -OutDll "Oritasy_0.0.9.193C.dll" -RspPath "%ROOT%obj\compile_c.rsp" -Define ORITASY_COMBINED -SourcesRsp "%ROOT%build_combined_sources.rsp"
if errorlevel 1 exit /b 1
"%CSC%" /noconfig @"%ROOT%obj\compile_c.rsp"
if errorlevel 1 (
  echo STANDARD C BUILD FAILED
  exit /b 1
)
echo Built Oritasy_0.0.9.193C.dll
endlocal
