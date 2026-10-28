param(
    [ValidateSet('1.6','1.7','Both')][string]$GameVersion = 'Both',
    [string]$GameRoot,
    [string]$GameRoot16,
    [string]$GameRoot17,
    [string]$Workshop = 'D:\STEAM\steamapps\workshop\content\294100',
    [string]$Dotnet
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Build-Environment.ps1')
$project = Split-Path $PSScriptRoot -Parent
$compiler = Get-SummonerDotnet $Dotnet
$versions = if ($GameVersion -eq 'Both') { @('1.6','1.7') } else { @($GameVersion) }
foreach ($version in $versions) {
    $requested = if ($version -eq '1.6') { $GameRoot16 } else { $GameRoot17 }
    if (!$requested -and $GameVersion -ne 'Both') { $requested = $GameRoot }
    $root = Get-SummonerGameRoot $version $requested
    $managed = Get-SummonerManaged $version $root
    & $compiler build (Join-Path $project 'Source\RimModSummoner.csproj') -c Release --nologo "-p:RimWorldVersion=$version" "-p:GameDir=$managed" "-p:Workshop=$Workshop"
    if ($LASTEXITCODE -ne 0) { throw "Summoner $version build failed." }
}
