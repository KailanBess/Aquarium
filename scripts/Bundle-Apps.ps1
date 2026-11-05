param([Parameter(Mandatory)][string]$PublishDirectory, [Parameter(Mandatory)][string]$FishNetDirectory, [Parameter(Mandatory)][string]$FishTankDirectory)
$ErrorActionPreference = 'Stop'
$bundle = Join-Path $PublishDirectory 'Bundled'
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
$manifest = @()
foreach ($app in @(
    @{ Name = 'FishTank'; Directory = $FishTankDirectory; Id = 'aquarium.security' },
    @{ Name = 'FishNet'; Directory = $FishNetDirectory; Id = 'aquarium.media' }
)) {
    if (!(Test-Path -LiteralPath (Join-Path $app.Directory ($app.Name + '.exe')))) { throw ('Build the hosting-compatible ' + $app.Name + ' before bundling.') }
    $zip = Join-Path $bundle ($app.Name + '.zip')
    Compress-Archive -Path (Join-Path $app.Directory '*') -DestinationPath $zip -Force
    $manifest += @{ appId = $app.Id; version = 'hosting-preview-bundled'; file = $app.Name + '.zip'; sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'bundled.json') -Encoding utf8
Write-Output 'Bundle includes hosting-compatible FishTank and FishNet builds. FishBowl stays optional.'
