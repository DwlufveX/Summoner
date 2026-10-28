param([string]$ModRoot = (Join-Path $PSScriptRoot '..\Mod'))
$ErrorActionPreference = 'Stop'
$definitions = @{}
Get-ChildItem -LiteralPath $ModRoot -Recurse -Filter '*.xml' -File | ForEach-Object {
    [xml]$xml = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
    if ($xml.DocumentElement.Name -eq 'Defs') {
        foreach ($node in $xml.DocumentElement.ChildNodes) {
            if ($node.NodeType -ne 'Element' -or !$node.defName) { continue }
            $key = $node.Name + ':' + $node.defName
            if ($definitions.ContainsKey($key)) { throw "Duplicate definition: $key" }
            $definitions[$key] = $_.FullName
        }
    }
    foreach ($node in $xml.SelectNodes('//iconPath|//texPath')) {
        if ($node.InnerText.StartsWith('UI/Summoner/') -or $node.InnerText.StartsWith('Things/Pawn/Summoner/')) {
            $asset = Join-Path $ModRoot ('Textures\' + $node.InnerText.Replace('/','\') + '.png')
            if (!(Test-Path -LiteralPath $asset)) { throw "Missing original placeholder: $asset" }
        }
        if ($node.InnerText -eq 'Things/Pawn/Animal/SpiritWolf/SpiritWolf') {
            foreach ($direction in @('north','east','south','west')) {
                $asset = Join-Path $ModRoot ('Textures\Things\Pawn\Animal\SpiritWolf\SpiritWolf_' + $direction + '.png')
                if (!(Test-Path -LiteralPath $asset)) { throw "Missing spirit wolf texture: $asset" }
            }
        }
    }
}
[xml]$loadFolders = Get-Content -LiteralPath (Join-Path $ModRoot 'LoadFolders.xml') -Raw
[xml]$about = Get-Content -LiteralPath (Join-Path $ModRoot 'About\About.xml') -Raw
foreach ($version in @('1.6','1.7')) {
    $declared = @($about.ModMetaData.supportedVersions.li)
    if ($version -notin $declared) { throw "About.xml does not declare $version" }
    $paths = @($loadFolders.loadFolders.SelectSingleNode('v' + $version).li)
    if ($paths.Count -ne 2 -or $paths[0] -ne '/' -or $paths[1] -ne $version) { throw "Incorrect $version load folders" }
    $assemblies = @(Get-ChildItem -LiteralPath (Join-Path $ModRoot "$version\Assemblies") -Filter '*.dll')
    if ($assemblies.Count -ne 1 -or $assemblies[0].Name -ne 'RimModSummoner.dll') { throw "Unexpected $version dependency assembly" }
    $framework = if ($version -eq '1.6') { '.NETFramework,Version=v4.8' } else { '.NETCoreApp,Version=v10.0' }
    $bytes = [IO.File]::ReadAllBytes($assemblies[0].FullName)
    if (![Text.Encoding]::UTF8.GetString($bytes).Contains($framework)) { throw "Wrong target framework in $version DLL" }
}
$allDlls = @(Get-ChildItem -LiteralPath $ModRoot -Recurse -Filter '*.dll' -File)
if ($allDlls.Count -ne 2) { throw 'Package must contain exactly two version-specific mod DLLs; remove legacy root or test/dependency DLLs.' }
Write-Output ("PASS: XML, {0} unique typed definitions, textures, both framework targets and isolated load folders." -f $definitions.Count)
if ($definitions.Count -eq 0) { Write-Output 'Development scaffold: no gameplay definitions are implemented yet.' }
