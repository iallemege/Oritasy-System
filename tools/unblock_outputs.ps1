# Strip Mark-of-the-Web so Windows does not prompt on freshly built DLLs.
param(
  [string]$Root = "",
  [string]$Dist = "",
  [string]$Plugins = ""
)
$ErrorActionPreference = "SilentlyContinue"

function Unblock-One([string]$path) {
  if ([string]::IsNullOrEmpty($path) -or -not (Test-Path -LiteralPath $path)) { return }
  try { Unblock-File -LiteralPath $path } catch {}
  try {
    $ads = $path + ":Zone.Identifier"
    if (Test-Path -LiteralPath $ads) { Remove-Item -LiteralPath $ads -Force }
  } catch {}
}

function Unblock-Glob([string]$dir, [string]$filter) {
  if ([string]::IsNullOrEmpty($dir) -or -not (Test-Path -LiteralPath $dir)) { return }
  Get-ChildItem -LiteralPath $dir -Filter $filter -File | ForEach-Object { Unblock-One $_.FullName }
}

if ($Root) {
  Unblock-Glob $Root "Oritasy*.dll"
  Unblock-Glob $Root "WeXon.dll"
  Unblock-One (Join-Path $Root "obj\R278Core.dll")
  Unblock-One (Join-Path $Root "obj\R278CoreD.dll")
  Unblock-Glob (Join-Path $Root "CdkExtractor") "CdkExtractor.exe"
  Unblock-Glob (Join-Path $Root "MissileEditor") "MissileEditor.exe"
  Unblock-Glob (Join-Path $Root "MissileEditor") "MissileEditor.dll"
}
if ($Dist) {
  Unblock-Glob $Dist "Oritasy*.dll"
  Unblock-Glob $Dist "WeXon.dll"
  Unblock-Glob $Dist "MissileEditor.dll"
  Unblock-Glob $Dist "MissileEditor.exe"
}
if ($Plugins) {
  Unblock-Glob $Plugins "Oritasy*.dll"
  Unblock-Glob $Plugins "MissileEditor.dll"
  Unblock-Glob $Plugins "WeXon.dll"
}
