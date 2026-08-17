# Writes a csc response file with flags, /r refs, and source list.
param(
  [Parameter(Mandatory = $true)][string]$OutDll,
  [Parameter(Mandatory = $true)][string]$RspPath,
  [string]$Define = "",
  [string]$SourcesRsp = "",
  [string]$LoaderDir = "",
  [switch]$Loader
)
$ErrorActionPreference = "Stop"
$game = "d:\Steam\steamapps\common\Nuclear Option"
$managed = Join-Path $game "NuclearOption_Data\Managed"
$bep = Join-Path $game "BepInEx\core"

$refs = New-Object System.Collections.Generic.List[string]
foreach ($p in @(
  (Join-Path $managed "mscorlib.dll"),
  (Join-Path $managed "netstandard.dll"),
  (Join-Path $managed "System.dll"),
  (Join-Path $managed "System.Core.dll"),
  (Join-Path $bep "BepInEx.dll")
)) { $refs.Add([string]$p) }
if (-not $Loader) {
  $refs.Add((Join-Path $bep "0Harmony.dll"))
  foreach ($p in @(
    (Join-Path $managed "Assembly-CSharp.dll"),
    (Join-Path $managed "Mirage.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.PhysicsModule.dll"),
    (Join-Path $managed "UnityEngine.IMGUIModule.dll"),
    (Join-Path $managed "UnityEngine.TextRenderingModule.dll"),
    (Join-Path $managed "UnityEngine.InputLegacyModule.dll"),
    (Join-Path $managed "UnityEngine.ImageConversionModule.dll"),
    (Join-Path $managed "UnityEngine.UI.dll"),
    (Join-Path $managed "UnityEngine.UIModule.dll"),
    (Join-Path $managed "UnityEngine.AudioModule.dll"),
    (Join-Path $managed "UnityEngine.UnityWebRequestModule.dll"),
    (Join-Path $managed "UnityEngine.UnityWebRequestAudioModule.dll"),
    (Join-Path $managed "UnityEngine.JSONSerializeModule.dll"),
    (Join-Path $managed "UnityEngine.AssetBundleModule.dll")
  )) { $refs.Add([string]$p) }
} else {
  $refs.Add((Join-Path $managed "UnityEngine.CoreModule.dll"))
  $refs.Add((Join-Path $managed "UnityEngine.dll"))
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("/nostdlib")
$lines.Add("/nologo")
$lines.Add("/optimize+")
$lines.Add("/debug-")
$lines.Add("/target:library")
$lines.Add("/platform:anycpu")
$lines.Add("/langversion:5")
if ($Define) { $lines.Add("/define:" + $Define) }
$lines.Add("/out:" + '"' + $OutDll + '"')
foreach ($r in $refs) { $lines.Add("/r:" + '"' + $r + '"') }

if ($SourcesRsp -and (Test-Path $SourcesRsp)) {
  Get-Content -LiteralPath $SourcesRsp | ForEach-Object { $lines.Add($_) }
}
if ($LoaderDir) {
  foreach ($name in @("AssemblyInfo.cs","RtEnc.cs","RmGate.cs","RmPack.cs","Host.cs")) {
    $lines.Add((Join-Path $LoaderDir $name))
  }
}

$dir = Split-Path -Parent $RspPath
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[IO.File]::WriteAllLines($RspPath, $lines.ToArray(), [Text.Encoding]::ASCII)
