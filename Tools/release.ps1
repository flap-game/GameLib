<#
.SYNOPSIS
  Bump every GameLib package to one version (lockstep) and optionally tag it.
.DESCRIPTION
  Rewrites "version" and internal com.flapgame.gamelib.* dependency versions in every
  Packages/com.flapgame.gamelib.*/package.json, GameLibVersion.Current, and turns the
  "Unreleased" CHANGELOG heading into today's date.
  With -Tag (run after committing) it creates:
    v<version>                  - used by git URL installs (?path=...#v<version>)
    <package>/<version>         - one per package, for OpenUPM gitTagPrefix
.EXAMPLE
  ./Tools/release.ps1 -Version 0.2.0
  git commit -am "Release 0.2.0"
  ./Tools/release.ps1 -Version 0.2.0 -Tag
#>
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')] [string] $Version,
    [switch] $Tag
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$packages = Get-ChildItem (Join-Path $root 'Packages') -Directory -Filter 'com.flapgame.gamelib.*'

if ($Tag) {
    if (git -C $root status --porcelain) { throw 'Working tree is dirty; commit the release first.' }
    git -C $root tag "v$Version"
    foreach ($p in $packages) { git -C $root tag "$($p.Name)/$Version" }
    Write-Host "Tagged v$Version and $($packages.Count) package tags. Push with: git push origin --tags"
    return
}

$date = Get-Date -Format 'yyyy-MM-dd'
foreach ($p in $packages) {
    $jsonPath = Join-Path $p.FullName 'package.json'
    $json = Get-Content $jsonPath -Raw | ConvertFrom-Json
    $json.version = $Version
    foreach ($dep in @($json.dependencies.PSObject.Properties)) {
        if ($dep.Name.StartsWith('com.flapgame.gamelib.')) { $dep.Value = $Version }
    }
    ($json | ConvertTo-Json -Depth 10) + "`n" | Set-Content -NoNewline -Encoding utf8NoBOM $jsonPath

    $changelog = Join-Path $p.FullName 'CHANGELOG.md'
    if (Test-Path $changelog) {
        $text = Get-Content $changelog -Raw
        $text = $text -replace '## \[[^\]]+\] - Unreleased', "## [$Version] - $date"
        Set-Content -NoNewline -Encoding utf8NoBOM $changelog $text
    }
}

$versionCs = Join-Path $root 'Packages/com.flapgame.gamelib.core/Runtime/GameLibVersion.cs'
(Get-Content $versionCs -Raw) -replace 'Current = "[^"]*"', "Current = `"$Version`"" |
    Set-Content -NoNewline -Encoding utf8NoBOM $versionCs

Write-Host "Bumped $($packages.Count) packages to $Version. Review, commit, then rerun with -Tag."
