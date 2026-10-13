param(
    [string]$GameRoot = 'D:\STEAM\steamapps\common\RimWorld',
    [string]$Workshop = 'D:\STEAM\steamapps\workshop\content\294100'
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$managed = Join-Path $GameRoot 'RimWorldWin64_Data\Managed'
dotnet build (Join-Path $project 'Source\RimModSummoner.csproj') -c Release --nologo "-p:GameDir=$managed" "-p:Workshop=$Workshop"
if ($LASTEXITCODE -ne 0) { throw 'Summoner build failed.' }
