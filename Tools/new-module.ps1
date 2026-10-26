<#
.SYNOPSIS
  Scaffold a new GameLib package under Packages/.
.EXAMPLE
  ./Tools/new-module.ps1 -Module ads.max -DisplayName "GameLib Ads - AppLovin MAX" -DependsOn ads
#>
param(
    [Parameter(Mandatory)] [string] $Module,       # e.g. "haptics", "ads.max"
    [Parameter(Mandatory)] [string] $DisplayName,
    [string] $Description = "",
    [string[]] $DependsOn = @()                    # other modules, e.g. "ads"; core is always added
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$prefix = 'com.flapgame.gamelib.'
$name = "$prefix$Module"
$dir = Join-Path $root "Packages/$name"
if (Test-Path $dir) { throw "$name already exists" }

$core = Get-Content (Join-Path $root "Packages/${prefix}core/package.json") -Raw | ConvertFrom-Json
$version = $core.version

# FlapGame.GameLib.Ads.Max from "ads.max"
function To-Pascal([string] $m) { ($m.Split('.') | ForEach-Object { $_.Substring(0,1).ToUpper() + $_.Substring(1) }) -join '.' }
$asm = "FlapGame.GameLib.$(To-Pascal $Module)"

$deps = [ordered]@{ "${prefix}core" = $version }
$asmRefs = @('FlapGame.GameLib.Core')
foreach ($d in $DependsOn | Where-Object { $_ -ne 'core' }) {
    $deps["$prefix$d"] = $version
    $asmRefs += "FlapGame.GameLib.$(To-Pascal $d)"
}

function Write-Json($path, $obj) {
    New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
    ($obj | ConvertTo-Json -Depth 10) + "`n" | Set-Content -NoNewline -Encoding utf8NoBOM $path
}
function Asmdef($asmName, $refs, [bool] $editor, [bool] $tests) {
    [ordered]@{
        name = $asmName
        rootNamespace = 'FlapGame.GameLib'
        references = @($refs)
        includePlatforms = @($(if ($editor) { 'Editor' }))
        excludePlatforms = @()
        allowUnsafeCode = $false
        overrideReferences = $tests
        precompiledReferences = @($(if ($tests) { 'nunit.framework.dll' }))
        autoReferenced = -not $tests
        defineConstraints = @($(if ($tests) { 'UNITY_INCLUDE_TESTS' }))
        versionDefines = @()
        noEngineReferences = $false
    }
}

Write-Json "$dir/package.json" ([ordered]@{
    name = $name
    version = $version
    displayName = $DisplayName
    description = $Description
    unity = $core.unity
    author = $core.author
    repository = $core.repository
    keywords = @('gamelib')
    dependencies = $deps
})
Write-Json "$dir/Runtime/$asm.asmdef" (Asmdef $asm $asmRefs $false $false)
Write-Json "$dir/Tests/Editor/$asm.Tests.Editor.asmdef" (Asmdef "$asm.Tests.Editor" (@($asm) + $asmRefs + 'UnityEngine.TestRunner', 'UnityEditor.TestRunner') $true $true)
New-Item -ItemType Directory -Force "$dir/Documentation~" | Out-Null
"# Changelog`n`n## [$version] - Unreleased`n- Package skeleton.`n" | Set-Content -NoNewline -Encoding utf8NoBOM "$dir/CHANGELOG.md"
$fence = '```'
@"
# $DisplayName

$Description

Install:

$fence
https://github.com/flap-game/GameLib.git?path=Packages/$name#v$version
$fence
"@ + "`n" | Set-Content -NoNewline -Encoding utf8NoBOM "$dir/README.md"

# Register as testable in the dev project
$manifestPath = Join-Path $root 'Packages/manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.testables = @($manifest.testables + $name | Sort-Object -Unique)
Write-Json $manifestPath $manifest

Write-Host "Created $name ($asm) at $version"
