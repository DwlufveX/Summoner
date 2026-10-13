param(
    [string]$RunName = ('test-' + (Get-Date -Format 'yyyyMMdd-HHmmss')),
    [string]$GameRoot = 'D:\STEAM\steamapps\common\RimWorld',
    [string]$Workshop = 'D:\STEAM\steamapps\workshop\content\294100'
)
$ErrorActionPreference = 'Stop'
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid run name' }
# Existing player sessions remain running. This process has its own save-data root.
$project = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Build.ps1') -GameRoot $GameRoot -Workshop $Workshop
$gameDir = $GameRoot
$run = Join-Path $project ('TestRuns\' + $RunName)
if (Test-Path -LiteralPath $run) { throw 'Run already exists' }
New-Item -ItemType Directory -Path (Join-Path $run 'Config') -Force | Out-Null
@'
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData><version>1.6.4871</version><activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>ludeon.rimworld.royalty</li><li>oskarpotocki.vanillafactionsexpanded.core</li><li>vanillaexpanded.vpsycastse</li><li>rimmod.summoner</li></activeMods><knownExpansions><li>ludeon.rimworld.royalty</li></knownExpansions></ModsConfigData>
'@ | Set-Content -Encoding utf8 (Join-Path $run 'Config\ModsConfig.xml')
@'
<?xml version="1.0" encoding="utf-8"?>
<PrefsData><runInBackground>True</runInBackground><pauseOnLoad>False</pauseOnLoad><langFolderName>ChineseSimplified</langFolderName><screenWidth>1024</screenWidth><screenHeight>768</screenHeight><fullscreen>False</fullscreen></PrefsData>
'@ | Set-Content -Encoding utf8 (Join-Path $run 'Config\Prefs.xml')
$target = Join-Path $gameDir 'Mods\RimModSummoner'
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $project 'Mod\*') -Destination $target -Recurse -Force
$arguments = @('-screen-fullscreen','0','-screen-width','1024','-screen-height','768',"-savedatafolder=$run",'-logFile',(Join-Path $run 'Player.log'),'-quicktest','-disable-compute-shaders','-summoner-selftest')
$process = Start-Process -FilePath (Join-Path $gameDir 'RimWorldWin64.exe') -WorkingDirectory $gameDir -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "PID=$($process.Id)"
Write-Output "RUN=$run"
