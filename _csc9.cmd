@echo off
setlocal EnableExtensions
set UNITY=F:\nuc\2022.3.62f2
set "DOTNET=%UNITY%\Editor\Data\NetCoreRuntime\dotnet.exe"
set "CSCDLL=%UNITY%\Editor\Data\DotNetSdkRoslyn\csc.dll"
set "DOTNET_CLI_HOME=%~dp0..\_cache\dotnet-cli"
set "NUGET_PACKAGES=%~dp0..\_cache\nuget"
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set DOTNET_CLI_TELEMETRY_OPTOUT=1
if not exist "%DOTNET%" (
  echo Unity dotnet.exe not found
  exit /b 1
)
if not exist "%CSCDLL%" (
  echo Unity csc.dll not found
  exit /b 1
)
if not exist "%DOTNET_CLI_HOME%" mkdir "%DOTNET_CLI_HOME%"
"%DOTNET%" exec "%CSCDLL%" /shared /utf8output /deterministic %* /langversion:10.0
endlocal & exit /b %ERRORLEVEL%
