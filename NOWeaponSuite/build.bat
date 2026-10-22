@echo off
setlocal EnableExtensions
rem Weapons-only pack (WeXon) - sources via @"rsp" (CMD length safe).
set GAME=d:\Steam\steamapps\common\Nuclear Option
set MANAGED=%GAME%\NuclearOption_Data\Managed
set BEP=%GAME%\BepInEx\core
set OUT=%~dp0WeXon.dll
set CSC=%~dp0..\_csc9.cmd
set DIST=%~dp0..\dist
set RSP=%~dp0wexon_sources.rsp
cd /d "%~dp0"

if exist "%RSP%" del /f /q "%RSP%"
for %%F in ("%~dp0src\*.cs") do >>"%RSP%" echo src\%%~nxF

call "%CSC%" /noconfig /nostdlib /nologo /optimize+ /target:library /platform:anycpu /langversion:10 ^
  /out:"%OUT%" ^
  /r:"%MANAGED%\mscorlib.dll" ^
  /r:"%MANAGED%\netstandard.dll" ^
  /r:"%MANAGED%\System.dll" ^
  /r:"%MANAGED%\System.Core.dll" ^
  /r:"%BEP%\BepInEx.dll" ^
  /r:"%BEP%\0Harmony.dll" ^
  /r:"%MANAGED%\Assembly-CSharp.dll" ^
  /r:"%MANAGED%\Mirage.dll" ^
  /r:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  /r:"%MANAGED%\UnityEngine.dll" ^
  /r:"%MANAGED%\UnityEngine.PhysicsModule.dll" ^
  /r:"%MANAGED%\UnityEngine.IMGUIModule.dll" ^
  /r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" ^
  /r:"%MANAGED%\UnityEngine.InputLegacyModule.dll" ^
  /r:"%MANAGED%\UnityEngine.ImageConversionModule.dll" ^
  /r:"%MANAGED%\UnityEngine.JSONSerializeModule.dll" ^
  @"%RSP%"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

echo Built: %OUT%
if not exist "%DIST%" mkdir "%DIST%"
copy /Y "%OUT%" "%DIST%\WeXon.dll" >nul
echo Copied to dist: %DIST%\WeXon.dll

rem Ship AAM-IV + TGM-85 visual assets next to optional WeXon pack
if not exist "%DIST%\WeXonAssets" mkdir "%DIST%\WeXonAssets"
if exist "%~dp0assets\AAM-IV.obj" copy /Y "%~dp0assets\AAM-IV.obj" "%DIST%\WeXonAssets\" >nul
if exist "%~dp0assets\texture.Aircraft_export.png" copy /Y "%~dp0assets\texture.Aircraft_export.png" "%DIST%\WeXonAssets\" >nul
if exist "%~dp0assets\VeyrnAam_icon.png" copy /Y "%~dp0assets\VeyrnAam_icon.png" "%DIST%\WeXonAssets\" >nul
for %%A in (  su_kh38_mt_missile_c.jpg su_kh38_mt_missile_n_ao.jpg su_kh38_mt_missile_n_n.jpg su_kh38_mt_missile_n_s.jpg su_kh_38mt.mtl) do (
  if exist "%~dp0assets\%%A" copy /Y "%~dp0assets\%%A" "%DIST%\WeXonAssets\" >nul
)
echo.
echo Packs: build_wexon / build_oritasy_air / build_split / build_combined
echo Full build + install:  ..\build_combined.bat
endlocal
