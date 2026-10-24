param(
    [switch]$Deploy,
    [string]$Target,
    [string]$GameRoot
)
$ErrorActionPreference = 'Stop'

# 游戏根目录：优先用参数，否则按 "仓库放在游戏目录的 mods/ 下" 的默认约定
if (-not $GameRoot) { $GameRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../AliceInCradle_ver030')) }
if (-not (Test-Path (Join-Path $GameRoot 'AliceInCradle_Data/Managed/Assembly-CSharp.dll'))) {
    throw "找不到游戏程序集，请用 -GameRoot 指定游戏根目录（含 AliceInCradle_Data 的那一层）：$GameRoot"
}

$managed = Join-Path $GameRoot 'AliceInCradle_Data/Managed'
$output  = Join-Path $PSScriptRoot 'bin/TeleportAnywhere.dll'
New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null

$refs = @(
    (Join-Path $GameRoot 'BepInEx/core/BepInEx.dll'),
    (Join-Path $GameRoot 'BepInEx/core/0Harmony.dll')
)
foreach ($name in @('Assembly-CSharp','unsafeAssem','better','pixelliner','netstandard','UnityEngine',
                    'UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.InputLegacyModule',
                    'UnityEngine.TextRenderingModule','Unity.InputSystem')) {
    $refs += Join-Path $managed ($name + '.dll')
}

$compilerArgs = @('/nologo','/target:library','/codepage:65001',('/out:' + $output))
$compilerArgs += $refs | ForEach-Object { '/reference:' + $_ }
foreach ($name in @('TeleportAnywhere.cs','TeleportInput.cs','TeleportPanel.cs','TeleportNativeMap.cs')) {
    $compilerArgs += Join-Path $PSScriptRoot $name
}

& 'C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe' @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Teleport Anywhere compilation failed' }

if ($Deploy -or $Target) {
    if (-not $Target) { $Target = Join-Path $GameRoot 'BepInEx/plugins/TeleportAnywhere' }
    New-Item -ItemType Directory -Path $Target -Force | Out-Null
    Copy-Item -LiteralPath $output -Destination $Target -Force
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'README.md')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $Target -Force
    }
}

Get-FileHash -LiteralPath $output -Algorithm SHA256