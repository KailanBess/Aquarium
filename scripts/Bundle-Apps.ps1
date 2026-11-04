param([Parameter(Mandatory)][string]$PublishDirectory, [Parameter(Mandatory)][string]$FishNetDirectory)
$ErrorActionPreference = 'Stop'
$bundle = Join-Path $PublishDirectory 'Bundled'
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
$headers = @{ 'User-Agent' = 'Aquarium-build/0.3' }
$release = Invoke-RestMethod -Headers $headers -Uri 'https://api.github.com/repos/KailanBess/FishTank/releases/tags/preview-v0.15.5'
if ($release.draft) { throw 'Draft releases cannot be bundled.' }
$asset = @($release.assets | Where-Object name -eq 'FishTank-desktop-preview.zip')
if ($asset.Count -ne 1 -or $asset[0].browser_download_url -notlike 'https://github.com/KailanBess/FishTank/releases/download/*') { throw 'No approved FishTank package.' }
$tank = Join-Path $bundle 'FishTank.zip'
Invoke-WebRequest -Headers $headers -Uri $asset[0].browser_download_url -OutFile $tank
$expected = [string]$asset[0].digest
if ($expected -match '^sha256:([a-fA-F0-9]{64})$') { $expected = $Matches[1] }
else {
    $sumAsset = @($release.assets | Where-Object name -eq 'SHA256SUMS.txt')
    if ($sumAsset.Count -ne 1 -or $sumAsset[0].browser_download_url -notlike 'https://github.com/KailanBess/FishTank/releases/download/*') { throw 'No published checksums.' }
    $manifest = (Invoke-WebRequest -Headers $headers -Uri $sumAsset[0].browser_download_url).Content
    $match = [regex]::Match($manifest, '(?m)^([a-fA-F0-9]{64})\s+\*?FishTank-desktop-preview\.zip\s*$')
    if (!$match.Success) { throw 'No package checksum.' }
    $expected = $match.Groups[1].Value
}
$tankHash = (Get-FileHash -LiteralPath $tank -Algorithm SHA256).Hash.ToLowerInvariant()
if ($tankHash -ne $expected) { throw 'FishTank package checksum mismatch.' }
if (!(Test-Path -LiteralPath (Join-Path $FishNetDirectory 'FishNet.exe'))) { throw 'Publish FishNet before bundling.' }
$net = Join-Path $bundle 'FishNet.zip'
Compress-Archive -Path (Join-Path $FishNetDirectory '*') -DestinationPath $net -Force
$netHash = (Get-FileHash -LiteralPath $net -Algorithm SHA256).Hash.ToLowerInvariant()
@(
    @{ appId = 'aquarium.security'; version = $release.tag_name; file = 'FishTank.zip'; sha256 = $tankHash },
    @{ appId = 'aquarium.media'; version = '0.2.0-bundled'; file = 'FishNet.zip'; sha256 = $netHash }
) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'bundled.json') -Encoding utf8
Write-Output 'Complete bundle includes FishTank and FishNet. FishBowl stays optional.'
