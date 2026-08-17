@echo off
rem Regenerates build_combined_sources.rsp from every .cs under WeXon + TGM-85 + CI22XE src.
rem This keeps the combined csc command under Windows' ~8191 cmdline limit
rem (sources go through @"rsp", not the command line).
setlocal EnableExtensions
set ROOT=%~dp0
cd /d "%ROOT%"
set RSP=%ROOT%build_combined_sources.rsp
if exist "%RSP%" del /f /q "%RSP%"
rem Stable-ish order: WeXon, TGM-85, then Oritasy air. New .cs auto-included.
for %%F in ("%ROOT%NOWeaponSuite\src\*.cs") do >>"%RSP%" echo NOWeaponSuite\src\%%~nxF
for %%F in ("%ROOT%Kh85MT\src\*.cs") do >>"%RSP%" echo Kh85MT\src\%%~nxF
for %%F in ("%ROOT%CI22XE\src\*.cs") do >>"%RSP%" echo CI22XE\src\%%~nxF
if not exist "%RSP%" (
  echo FAILED: no sources for rsp
  exit /b 1
)
exit /b 0
