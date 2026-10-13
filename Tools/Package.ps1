param(
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\Dist'),
    [string]$GameRoot = 'D:\STEAM\steamapps\common\RimWorld',
    [string]$Workshop = 'D:\STEAM\steamapps\workshop\content\294100'
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Build.ps1') -GameRoot $GameRoot -Workshop $Workshop
& (Join-Path $PSScriptRoot 'Verify-Package.ps1')
[xml]$projectXml = Get-Content -LiteralPath (Join-Path $project 'Source\RimModSummoner.csproj') -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$stage = Join-Path $OutputDir ('package-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$mod = Join-Path $stage 'RimModSummoner'
New-Item -ItemType Directory -Force -Path $mod | Out-Null
foreach ($folder in @('About','Defs','Patches','Languages','Textures')) {
    Copy-Item -LiteralPath (Join-Path $project ('Mod\' + $folder)) -Destination $mod -Recurse
}
New-Item -ItemType Directory -Force -Path (Join-Path $mod 'Assemblies') | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'Mod\Assemblies\RimModSummoner.dll') -Destination (Join-Path $mod 'Assemblies')
Copy-Item -LiteralPath (Join-Path $project 'README.md') -Destination $mod
New-Item -ItemType Directory -Path (Join-Path $mod 'docs') -Force | Out-Null
foreach ($name in @('Summoner_v0.1_Design.md','MANUAL_TESTS.md','TEST_REPORT.md')) {
    Copy-Item -LiteralPath (Join-Path $project ('docs\' + $name)) -Destination (Join-Path $mod 'docs')
}
$archive = Join-Path $OutputDir ("RimModSummoner-v$version.zip")
Compress-Archive -LiteralPath $mod -DestinationPath $archive -Force
Write-Output $archive
