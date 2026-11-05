param([Parameter(Mandatory)][string]$PublishDirectory,[Parameter(Mandatory)][string]$FishNetDirectory,[Parameter(Mandatory)][string]$FishTankArchive,[string]$FishTankVersion='0.16.0',[string]$FishNetVersion='0.3.1')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$bundle=Join-Path $PublishDirectory 'Bundled'
New-Item -ItemType Directory -Path $bundle -Force | Out-Null
$netAssembly=Join-Path $FishNetDirectory 'FishNet.dll'
$netIdentity=[Reflection.AssemblyName]::GetAssemblyName($netAssembly)
if(!(Test-Path -LiteralPath (Join-Path $FishNetDirectory 'FishNet.exe')) -or $netIdentity.Name -ne 'FishNet' -or $netIdentity.Version -ne [Version]($FishNetVersion+'.0')){throw 'Publish the matching complete FishNet runtime before bundling.'}
$zip=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($FishTankArchive))
try {
 if(@($zip.Entries | Where-Object {$_.FullName -match '[/\\]' -or $_.FullName.Contains('..') -or $_.Length -gt 128MB}).Count){throw 'Unsafe FishTank package paths or size.'}
 $names=@($zip.Entries | ForEach-Object FullName)
 if(@($names | Sort-Object -Unique).Count -ne $names.Count -or $names -notcontains 'FishTank.exe' -or $names -notcontains 'FishTank.Cli.exe' -or $names -notcontains 'SHA256SUMS.txt'){throw 'Incomplete FishTank package.'}
 $reader=New-Object IO.StreamReader($zip.GetEntry('SHA256SUMS.txt').Open())
 try{$manifest=$reader.ReadToEnd()}finally{$reader.Dispose()}
 $hashes=@{}
 foreach($line in ($manifest -split '\r?\n')){
  if(!$line.Trim()){continue}
  if($line -notmatch '^([a-fA-F0-9]{64})  ([A-Za-z0-9_. -]+)$' -or $hashes.ContainsKey($Matches[2])){throw 'Invalid FishTank manifest.'}
  $hashes[$Matches[2]]=$Matches[1]
 }
 if($hashes.Count -ne $names.Count-1){throw 'Incomplete FishTank manifest.'}
 foreach($entry in $zip.Entries){
  if($entry.FullName -eq 'SHA256SUMS.txt'){continue}
  $sha=[Security.Cryptography.SHA256]::Create();$stream=$entry.Open()
  try{$hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}finally{$sha.Dispose();$stream.Dispose()}
  if($hash -ne $hashes[$entry.FullName]){throw 'FishTank package checksum mismatch.'}
 }
 $temporary=[IO.Path]::GetTempFileName()
 try {
  $inputStream=$zip.GetEntry('FishTank.exe').Open();$outputStream=[IO.File]::Open($temporary,[IO.FileMode]::Create,[IO.FileAccess]::Write)
  try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}
  $identity=[Reflection.AssemblyName]::GetAssemblyName($temporary)
  if($identity.Name -ne 'FishTank' -or $identity.Version -ne [Version]($FishTankVersion+'.0')){throw 'FishTank version/identity differs from the bundle manifest.'}
 }finally{Remove-Item -LiteralPath $temporary -Force}

}finally{$zip.Dispose()}
$tank=Join-Path $bundle 'FishTank.zip';Copy-Item -LiteralPath $FishTankArchive -Destination $tank
$net=Join-Path $bundle 'FishNet.zip';Compress-Archive -Path (Join-Path $FishNetDirectory '*') -DestinationPath $net -Force
@(
 @{appId='aquarium.security';version=('preview-v'+$FishTankVersion);file='FishTank.zip';sha256=(Get-FileHash $tank -Algorithm SHA256).Hash.ToLowerInvariant()},
 @{appId='aquarium.media';version=('v'+$FishNetVersion);file='FishNet.zip';sha256=(Get-FileHash $net -Algorithm SHA256).Hash.ToLowerInvariant()}
) | ConvertTo-Json | Set-Content (Join-Path $bundle 'bundled.json') -Encoding utf8
Write-Output 'Verified local builds of FishTank and FishNet are included. FishBowl remains an optional release installation.'
