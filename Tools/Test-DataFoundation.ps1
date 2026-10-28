param(
    [ValidateSet('1.6','1.7')][string]$GameVersion = '1.6',
    [string]$GameRoot,
    [string]$Workshop = 'D:\STEAM\steamapps\workshop\content\294100',
    [ValidateSet('full','regression','live-guard')][string]$Mode = 'regression'
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Build-Environment.ps1')
$GameRoot = Get-SummonerGameRoot $GameVersion $GameRoot
$managed = Get-SummonerManaged $GameVersion $GameRoot
$sdk = Get-SummonerDotnet
& (Join-Path $PSScriptRoot 'Build.ps1') -GameVersion $GameVersion -GameRoot $GameRoot -Workshop $Workshop
& $sdk build (Join-Path $project 'Tests\ScribeHost\ScribeHost.csproj') -c Release --nologo "-p:RimWorldVersion=$GameVersion" "-p:Workshop=$Workshop"
if ($LASTEXITCODE -ne 0) { throw 'Data foundation host build failed.' }
$run = Join-Path $project ('TestRuns\data-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + $Mode)
New-Item -ItemType Directory -Path $run | Out-Null
$hostFolder = Join-Path $project "Tests\ScribeHost\bin\$GameVersion\Release"
$hostArgs = @($managed, (Join-Path $project "Mod\$GameVersion\Assemblies\RimModSummoner.dll"), (Join-Path $run 'scribe-log.txt'), $Mode)
if ($GameVersion -eq '1.7') { & $sdk (Join-Path $hostFolder 'ScribeHost.dll') @hostArgs | Tee-Object -FilePath (Join-Path $run 'result.txt') }
else { & (Join-Path $hostFolder 'ScribeHost.exe') @hostArgs | Tee-Object -FilePath (Join-Path $run 'result.txt') }
$hostExit = $LASTEXITCODE
Write-Output "RESULT_DIR=$run"
if ($hostExit -ne 0) { throw "Data foundation $Mode check failed (exit $hostExit). See $run." }
