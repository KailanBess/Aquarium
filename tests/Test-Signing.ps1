$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot '../scripts/Sign-Optional.ps1'
$saved = @{}
foreach ($name in @('WINDOWS_SIGNING_PFX_BASE64','WINDOWS_SIGNING_PASSWORD','WINDOWS_TIMESTAMP_URL')) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $null)
}
try {
    $result = & $script -Files @('not-a-signing-target.fixture')
    if ($result -ne $false) { throw 'Missing credentials must remain explicitly unsigned.' }
    $env:WINDOWS_SIGNING_PFX_BASE64 = 'inert-fixture'
    $refused = $false
    try { & $script -Files @('not-a-signing-target.fixture') } catch { $refused = $_.Exception.Message -like '*incomplete*' }
    if (!$refused) { throw 'Partial signing configuration must fail before importing a certificate.' }
    $env:WINDOWS_SIGNING_PASSWORD = 'inert-fixture'
    $env:WINDOWS_TIMESTAMP_URL = 'http://example.invalid/timestamp'
    $refused = $false
    try { & $script -Files @('not-a-signing-target.fixture') } catch { $refused = $_.Exception.Message -like '*HTTPS*' }
    if (!$refused) { throw 'Insecure timestamp configuration must fail before importing a certificate.' }
    Write-Host 'Signing absence, partial-configuration and timestamp validation checks passed. No certificate was imported.'
}
finally {
    foreach ($entry in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
}
