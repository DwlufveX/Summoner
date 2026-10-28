function Get-SummonerDotnet([string]$Requested) {
    if ($Requested) { return $Requested }
    $local = Join-Path (Split-Path $PSScriptRoot -Parent) '.tools\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $local) { return $local }
    return (Get-Command dotnet -ErrorAction Stop).Source
}

function Get-SummonerGameRoot([string]$Version, [string]$Requested) {
    if ($Requested) { return (Resolve-Path -LiteralPath $Requested).Path }
    $config = Join-Path (Split-Path $PSScriptRoot -Parent) '.build.local.json'
    if (Test-Path -LiteralPath $config) {
        $paths = Get-Content -LiteralPath $config -Raw -Encoding UTF8 | ConvertFrom-Json
        $entry = $paths.gameRoots.PSObject.Properties[$Version]
        if ($entry -and $entry.Value) { return (Resolve-Path -LiteralPath $entry.Value).Path }
    }
    return 'D:\STEAM\steamapps\common\RimWorld'
}

function Get-SummonerManaged([string]$Version, [string]$GameRoot) {
    $versionFile = Join-Path $GameRoot 'Version.txt'
    if (!(Test-Path -LiteralPath $versionFile) -or (Get-Content -LiteralPath $versionFile -Raw) -notmatch ('^' + [regex]::Escape($Version) + '\.')) {
        throw "Game root does not contain RimWorld $Version`: $GameRoot. Set GameRoot$($Version.Replace('.','')) or .build.local.json."
    }
    $managed = if ($Version -eq '1.6') { Join-Path $GameRoot 'RimWorldWin64_Data\Managed' } else { $GameRoot }
    $dependencies = if ($Version -eq '1.6') { @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.IMGUIModule.dll') } else { @('Assembly-CSharp.dll','UnityStubs.dll') }
    foreach ($dependency in $dependencies) {
        if (!(Test-Path -LiteralPath (Join-Path $managed $dependency))) { throw "Missing $Version dependency: $managed\$dependency" }
    }
    return $managed
}
