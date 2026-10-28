#Requires -Version 7.4
param(
    [ValidateSet('1.6','1.7')][string]$GameVersion = '1.7',
    [ValidateSet('Archive','Gameplay')][string]$TestMode = 'Archive',
    [string]$GameRoot,
    [string]$Workshop = 'D:\STEAM\steamapps\workshop\content\294100',
    [string]$Dotnet,
    [string]$RunRoot = 'C:\SummonerTests',
    [int]$TimeoutSeconds = 600
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Build-Environment.ps1')
$project = Split-Path $PSScriptRoot -Parent
$absoluteRunRoot = [IO.Path]::GetFullPath($RunRoot).TrimEnd('\')
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
if ($absoluteRunRoot.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $absoluteRunRoot -eq $temporaryRoot) {
    throw 'RunRoot must be outside the system temp directory: RimWorld clears its temporary cache on exit.'
}
$GameRoot = Get-SummonerGameRoot $GameVersion $GameRoot
$managed = Get-SummonerManaged $GameVersion $GameRoot
$requiredContent = @('Data\Core\Defs\ThingDefs_Races\Races_Animal_WildCanines.xml')
if ($GameVersion -eq '1.7') { $requiredContent += 'Rendering\Shaders\SceneDefault.frag.glsl' }
else { $requiredContent += 'MonoBleedingEdge\EmbedRuntime\mono-2.0-bdwgc.dll' }
foreach ($file in $requiredContent) {
    if (!(Test-Path -LiteralPath (Join-Path $GameRoot $file))) { throw "Game resources incomplete: $file. Repair the source game before running tests." }
}
$sdk = Get-SummonerDotnet $Dotnet
& $sdk build (Join-Path $project 'Tests\GameSupport\GameSupport.csproj') -c Release --nologo "-p:RimWorldVersion=$GameVersion" "-p:GameDir=$managed" "-p:Workshop=$Workshop"
if ($LASTEXITCODE -ne 0) { throw 'Game test support build failed.' }
$run = Join-Path $RunRoot ('game-' + $GameVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$game = Join-Path $run 'Game'
$save = Join-Path $run 'SaveData'
New-Item -ItemType Directory -Path $game,(Join-Path $save 'Config'),(Join-Path $game 'Mods'),(Join-Path $run 'TempCache') | Out-Null
Get-ChildItem -LiteralPath $GameRoot -File | Where-Object { $_.Extension -notin @('.log','.rws') } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $game
}
$content = if ($GameVersion -eq '1.6') { @('Data','RimWorldWin64_Data','MonoBleedingEdge') } else { @('Data','Rendering') }
foreach ($folder in $content) {
    if ($folder -eq 'Data') {
        New-Item -ItemType Directory -Path (Join-Path $game 'Data') | Out-Null
        Copy-Item -LiteralPath (Join-Path $GameRoot 'Data\Core') -Destination (Join-Path $game 'Data') -Recurse
    } else { Copy-Item -LiteralPath (Join-Path $GameRoot $folder) -Destination $game -Recurse }
}
$harmony = Join-Path $game 'Mods\Harmony'
New-Item -ItemType Directory -Path $harmony | Out-Null
foreach ($folder in @('About',$GameVersion)) { Copy-Item -LiteralPath (Join-Path $Workshop "2009463077\$folder") -Destination $harmony -Recurse }
Copy-Item -LiteralPath (Join-Path $Workshop '2009463077\LoadFolders.xml') -Destination $harmony
$mod = Join-Path $game 'Mods\Summoner'
New-Item -ItemType Directory -Path $mod | Out-Null
foreach ($folder in @('About','Defs','Patches','Languages','Textures',$GameVersion)) {
    $source = Join-Path $project "Mod\$folder"
    if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $mod -Recurse }
}
Copy-Item -LiteralPath (Join-Path $project 'Mod\LoadFolders.xml') -Destination $mod
$test = Join-Path $game 'Mods\SummonerTests'
New-Item -ItemType Directory -Path (Join-Path $test 'Assemblies') | Out-Null
foreach ($folder in @('About','Defs')) { Copy-Item -LiteralPath (Join-Path $project "Tests\GameSupport\$folder") -Destination $test -Recurse }
Copy-Item -LiteralPath (Join-Path $project "Tests\GameSupport\bin\$GameVersion\Release\RimModSummonerTests.dll") -Destination (Join-Path $test 'Assemblies')
$version = ((Get-Content -LiteralPath (Join-Path $game 'Version.txt') -Raw).Trim() -split ' ')[0]
@"
<ModsConfigData><version>$version</version><activeMods><li>brrainz.harmony</li><li>ludeon.rimworld</li><li>rimmod.summoner</li><li>rimmod.summoner.tests</li></activeMods><knownExpansions /></ModsConfigData>
"@ | Set-Content -LiteralPath (Join-Path $save 'Config\ModsConfig.xml') -Encoding utf8
'<PrefsData><runInBackground>true</runInBackground><devMode>true</devMode><pauseOnLoad>false</pauseOnLoad><langFolderName>English</langFolderName></PrefsData>' | Set-Content -LiteralPath (Join-Path $save 'Config\Prefs.xml') -Encoding utf8
$exe = if ($GameVersion -eq '1.6') { 'RimWorldWin64.exe' } else { 'RimWorld.exe' }
$testFlag = if ($TestMode -eq 'Archive') { '-summoner-archive-test' } else { '-summoner-gameplay-test' }
$args = @('-quicktest',$testFlag, ('"-savedatafolder=' + $save + '"'))
if ($GameVersion -eq '1.6') { $args += @('-batchmode','-screen-fullscreen','0','-screen-width','1024','-screen-height','768','-logFile',('"' + (Join-Path $run 'Player.log') + '"')) } else { $args += @('-Headless','-NoCrashDialog') }
$process = Start-Process -FilePath (Join-Path $game $exe) -WorkingDirectory $game -ArgumentList $args -WindowStyle Hidden -PassThru -Environment @{ TEMP=(Join-Path $run 'TempCache'); TMP=(Join-Path $run 'TempCache') } -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log')
@{ version=$GameVersion; testMode=$TestMode; run=$run; pid=$process.Id; executable=(Join-Path $game $exe); gameVersion=$version } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'run.json') -Encoding utf8
Write-Output "RESULT_DIR=$run PID=$($process.Id)"
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
while (!$process.WaitForExit(1000)) {
    if ((Get-Date) -gt $deadline) {
        if ($process.Path -eq (Join-Path $game $exe)) { Stop-Process -Id $process.Id }
        throw "Game test timed out. See $run"
    }
}
$result = Join-Path $save ($TestMode.ToLower() + '-result.txt')
if (!(Test-Path -LiteralPath $result)) { throw "Game exited without a test result. See $run" }
$message = Get-Content -LiteralPath $result -Raw
Write-Output $message
if (!$message.StartsWith('PASS:')) { throw "Game archive verification failed. See $run" }
