param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\Dist'),
    [string]$GameRoot16,
    [string]$GameRoot17,
    [string]$Workshop = 'D:\STEAM\steamapps\workshop\content\294100'
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Build.ps1') -GameVersion Both -GameRoot16 $GameRoot16 -GameRoot17 $GameRoot17 -Workshop $Workshop
& (Join-Path $PSScriptRoot 'Verify-Package.ps1')
[xml]$projectXml = Get-Content -LiteralPath (Join-Path $project 'Source\RimModSummoner.csproj') -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$stage = Join-Path $OutputDir ('package-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$mod = Join-Path $stage 'RimModSummoner'
New-Item -ItemType Directory -Force -Path $mod | Out-Null
foreach ($folder in @('About','Defs','Patches','Languages','Textures')) {
    $sourceFolder = Join-Path $project ('Mod\' + $folder)
    if (Test-Path -LiteralPath $sourceFolder) {
        Copy-Item -LiteralPath $sourceFolder -Destination $mod -Recurse
    }
}
Copy-Item -LiteralPath (Join-Path $project 'Mod\LoadFolders.xml') -Destination $mod
foreach ($gameVersion in @('1.6','1.7')) {
    $assemblyFolder = Join-Path $mod "$gameVersion\Assemblies"
    New-Item -ItemType Directory -Path $assemblyFolder -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $project "Mod\$gameVersion\Assemblies\RimModSummoner.dll") -Destination $assemblyFolder
}
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination $mod
New-Item -ItemType Directory -Path (Join-Path $mod 'docs') -Force | Out-Null
foreach ($name in @('GAMEPLAY.md','ROADMAP.md','STATUS.md','COMPATIBILITY.md')) {
    Copy-Item -LiteralPath (Join-Path $project ('docs\' + $name)) -Destination (Join-Path $mod 'docs')
}
Copy-Item -LiteralPath (Join-Path $project 'docs\tasks') -Destination (Join-Path $mod 'docs') -Recurse
New-Item -ItemType Directory -Path (Join-Path $mod 'docs\Evidence') -Force | Out-Null
foreach ($evidence in @('dual-version','druid-contract')) {
    $sourceEvidence = Join-Path $project "docs\Evidence\$evidence"
    if (Test-Path -LiteralPath $sourceEvidence) { Copy-Item -LiteralPath $sourceEvidence -Destination (Join-Path $mod 'docs\Evidence') -Recurse }
}
New-Item -ItemType Directory -Path (Join-Path $mod 'docs\References') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'docs\References\召唤师流派系统设计需求v0.10.md') -Destination (Join-Path $mod 'docs\References')
& (Join-Path $PSScriptRoot 'Verify-Package.ps1') -ModRoot $mod
$archive = Join-Path $OutputDir ("RimModSummoner-v$version-1.6-1.7.zip")
Compress-Archive -LiteralPath $mod -DestinationPath $archive -Force
Write-Output $archive
