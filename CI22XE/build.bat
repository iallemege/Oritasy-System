@echo off
setlocal EnableExtensions
rem Aircraft-only pack (keep separate from combined Oritasy.dll)
rem Sources via @"rsp" to stay under Windows CMD ~8191 limit.
set GAME=d:\Steam\steamapps\common\Nuclear Option
set MANAGED=%GAME%\NuclearOption_Data\Managed
set BEP=%GAME%\BepInEx\core
set OUT=%~dp0OritasyAir.dll
set CSC=%~dp0..\_csc9.cmd
set DIST=%~dp0..\dist
set RSP=%~dp0oritasy_air_sources.rsp
cd /d "%~dp0"

if exist "%RSP%" del /f /q "%RSP%"
set HUDLIST=%~dp0..\hud_pack_files.txt
set ALL=%~dp0oritasy_air_sources_all.rsp
if exist "%ALL%" del /f /q "%ALL%"
for %%F in ("%~dp0src\*.cs") do >>"%ALL%" echo src\%%~nxF
findstr /V /I /G:"%HUDLIST%" "%ALL%" > "%RSP%"
if exist "%ALL%" del /f /q "%ALL%"

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
  /r:"%MANAGED%\UnityEngine.AnimationModule.dll" ^
  /r:"%MANAGED%\UnityEngine.IMGUIModule.dll" ^
  /r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" ^
  /r:"%MANAGED%\UnityEngine.InputLegacyModule.dll" ^
  /r:"%MANAGED%\UnityEngine.ImageConversionModule.dll" ^
  /r:"%MANAGED%\UnityEngine.UI.dll" ^
  /r:"%MANAGED%\UnityEngine.UIModule.dll" ^
  /r:"%MANAGED%\UnityEngine.AudioModule.dll" ^
  /r:"%MANAGED%\UnityEngine.UnityWebRequestModule.dll" ^
  /r:"%MANAGED%\UnityEngine.UnityWebRequestAudioModule.dll" ^
  /r:"%MANAGED%\UnityEngine.AssetBundleModule.dll" ^
  @"%RSP%"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

echo Built: %OUT%
if not exist "%DIST%" mkdir "%DIST%"
copy /Y "%OUT%" "%DIST%\OritasyAir.dll" >nul
echo Copied to dist: %DIST%\OritasyAir.dll

rem Custom music drop-in folder (also created at runtime)
if not exist "%DIST%\OritasyMusic" mkdir "%DIST%\OritasyMusic"
for %%M in (menu start tactical strategic combat takeoff victory defeat) do (
  if not exist "%DIST%\OritasyMusic\%%M" mkdir "%DIST%\OritasyMusic\%%M"
)
echo.
echo Packs: build_wexon / build_oritasy_air / build_split / build_combined
echo Full build + install:  ..\build_combined.bat
endlocal
