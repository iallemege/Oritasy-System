@echo off
setlocal EnableExtensions
rem Standalone Kh-85MT / TGM-85 (AGM-68 donor + Kh-85MT visual) - v1.8.24
rem Override install target: set KH85_GAME=D:\path\to\Nuclear Option
if defined KH85_GAME (
  set "GAME=%KH85_GAME%"
) else (
  set "GAME=d:\Steam\steamapps\common\Nuclear Option"
)
set MANAGED=%GAME%\NuclearOption_Data\Managed
set BEP=%GAME%\BepInEx\core
set PLUGINS=%GAME%\BepInEx\plugins
set OUT=%~dp0Kh85MT.dll
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set ASSETS_SRC=%~dp0assets
set ASSETS_DST=%PLUGINS%\WeXonAssets
set ASSETS_LEGACY=%PLUGINS%\Kh85MTAssets

if not exist "%CSC%" (
  echo csc.exe not found
  exit /b 1
)
if not exist "%MANAGED%\Assembly-CSharp.dll" (
  echo Assembly-CSharp.dll not found under GAME path
  exit /b 1
)

"%CSC%" /noconfig /nostdlib /nologo /optimize+ /target:library /platform:anycpu /langversion:5 ^
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
  /r:"%MANAGED%\UnityEngine.ImageConversionModule.dll" ^
  /r:"%MANAGED%\UnityEngine.IMGUIModule.dll" ^
  /r:"%MANAGED%\UnityEngine.InputLegacyModule.dll" ^
  /r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" ^
  /r:"%MANAGED%\UnityEngine.UI.dll" ^
  /r:"%MANAGED%\UnityEngine.UIModule.dll" ^
  /r:"%MANAGED%\Unity.TextMeshPro.dll" ^
  "%~dp0src\Plugin.cs" ^
  "%~dp0src\Kh85Visual.cs" ^
  "%~dp0src\Kh85Advanced.cs" ^
  "%~dp0src\Kh85CFlight.cs" ^
  "%~dp0src\Kh85AEcm.cs" ^
  "%~dp0src\Kh85BEcm.cs" ^
  "%~dp0src\Kh85EDecoy.cs" ^
  "%~dp0src\Kh85DArm.cs" ^
  "%~dp0src\Kh85SHyper.cs"

if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)

echo Built: %OUT%

if not exist "%ASSETS_DST%" mkdir "%ASSETS_DST%"
if not exist "%ASSETS_LEGACY%" mkdir "%ASSETS_LEGACY%"
for %%A in (Kh-85MT.obj Kh-85MT_icon.png su_kh38_mt_missile_c.jpg su_kh38_mt_missile_n_ao.jpg su_kh38_mt_missile_n_n.jpg su_kh38_mt_missile_n_s.jpg su_kh_38mt.mtl) do (
  if exist "%ASSETS_SRC%\%%A" (
    copy /Y "%ASSETS_SRC%\%%A" "%ASSETS_DST%\" >nul
    copy /Y "%ASSETS_SRC%\%%A" "%ASSETS_LEGACY%\" >nul
  )
)

rem Combined Oritasy already hosts TGM-85. Installing Kh85MT.dll alongside it double-patches missiles.
set HOSTED=0
for %%F in ("%PLUGINS%\Oritasy_*.dll") do set HOSTED=1
if "%HOSTED%"=="1" (
  echo TGM-85 is hosted inside Oritasy - skipped installing Kh85MT.dll
  echo Assets: %ASSETS_DST%
  echo Config: BepInEx\config\com.iallemege.kh85mt.cfg
  goto :kh85_done
)

if exist "%PLUGINS%\Kh85MT.dll" (
  del /f /q "%PLUGINS%\Kh85MT.dll" 2>nul
  if exist "%PLUGINS%\Kh85MT.dll" ren "%PLUGINS%\Kh85MT.dll" Kh85MT.dll.old 2>nul
)
copy /Y "%OUT%" "%PLUGINS%\Kh85MT.dll"
if errorlevel 1 (
  echo INSTALL FAILED - close the game and rebuild
  exit /b 1
)
del /f /q "%PLUGINS%\Kh85MT.dll.old" 2>nul

if exist "%PLUGINS%\Kh85MTSurvival.dll" del /f /q "%PLUGINS%\Kh85MTSurvival.dll" 2>nul
if exist "%PLUGINS%\Kh85MTSurvival.dll.old" del /f /q "%PLUGINS%\Kh85MTSurvival.dll.old" 2>nul

echo Installed: %PLUGINS%\Kh85MT.dll
echo Assets:    %ASSETS_DST%
echo Config:    BepInEx\config\com.iallemege.kh85mt.cfg
:kh85_done
endlocal
