param([string]$ModRoot = (Join-Path $PSScriptRoot '..\Mod'))
$ErrorActionPreference = 'Stop'
$definitions = @{}
Get-ChildItem -LiteralPath $ModRoot -Recurse -Filter '*.xml' -File | ForEach-Object {
    [xml]$xml = Get-Content -LiteralPath $_.FullName -Raw
    if ($xml.DocumentElement.Name -eq 'Defs') {
        foreach ($node in $xml.DocumentElement.ChildNodes) {
            if ($node.NodeType -ne 'Element' -or !$node.defName) { continue }
            $key = $node.Name + ':' + $node.defName
            if ($definitions.ContainsKey($key)) { throw "Duplicate definition: $key" }
            $definitions[$key] = $_.FullName
        }
    }
    foreach ($node in $xml.SelectNodes('//iconPath|//texPath')) {
        if ($node.InnerText.StartsWith('UI/Summoner/')) {
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
$assemblies = @(Get-ChildItem -LiteralPath (Join-Path $ModRoot 'Assemblies') -Filter '*.dll')
if ($assemblies.Count -ne 1 -or $assemblies[0].Name -ne 'RimModSummoner.dll') { throw 'Unexpected or bundled dependency assembly' }
Write-Output ("PASS: XML parse, {0} unique typed definitions, local UI/wolf texture references and dependency-free assembly package." -f $definitions.Count)
