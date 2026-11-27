param(
 [string]$Tag = '',
 [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
if(!$Tag){[xml]$project=Get-Content (Join-Path $PSScriptRoot '../src/Aquarium/Aquarium.csproj');$Tag='v'+[string]$project.Project.PropertyGroup.Version}
if($Tag -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$'){throw 'Use a numeric Aquarium release tag.'}
$directory=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $directory){if(@(Get-ChildItem -LiteralPath $directory -Force).Count){throw 'Choose an empty verification directory.'}}
else{New-Item -ItemType Directory -Path $directory | Out-Null}
$headers=@{'User-Agent'='Aquarium-public-release-verifier';Accept='application/vnd.github+json'}
if($env:GITHUB_TOKEN){$headers.Authorization='Bearer '+$env:GITHUB_TOKEN}
$api='https://api.github.com/repos/KailanBess/Aquarium'
$release=Invoke-RestMethod "$api/releases/tags/$Tag" -Headers $headers -TimeoutSec 60
if($release.draft -or $release.tag_name -ne $Tag -or @($release.assets).Count -gt 64){throw 'Release is not a supported public delivery.'}
$ref=Invoke-RestMethod "$api/git/ref/tags/$Tag" -Headers $headers -TimeoutSec 60
$target=$ref.object
for($depth=0;$target.type -eq 'tag' -and $depth -lt 8;$depth++){
 if($target.sha -notmatch '^[0-9a-f]{40}$'){throw 'Invalid tag object.'}
 $target=(Invoke-RestMethod "$api/git/tags/$($target.sha)" -Headers $headers -TimeoutSec 60).object
}
if($target.type -ne 'commit' -or $target.sha -notmatch '^[0-9a-f]{40}$'){throw 'Unsupported tag target.'}
$source=$target.sha
git merge-base --is-ancestor $source origin/main
if($LASTEXITCODE -ne 0){throw 'Release source is not on main.'}
$names=@($release.assets.name)
if(@($names | Sort-Object -Unique).Count -ne $names.Count){throw 'Ambiguous asset names.'}
$downloaded=@{};$verified=@();[long]$total=0
foreach($asset in $release.assets){
 if($asset.name -notmatch '^[A-Za-z0-9_.-]{1,128}$' -or $asset.name.Contains('..') -or $asset.size -le 0 -or $asset.size -gt 512MB){throw 'Unsupported asset name/size.'}
 $expected='https://github.com/KailanBess/Aquarium/releases/download/'+$Tag+'/'+$asset.name
 if($asset.browser_download_url -ne $expected -or $asset.digest -notmatch '^sha256:[a-f0-9]{64}$'){throw 'Asset URL or digest is invalid.'}
 $total+=$asset.size;if($total -gt 2GB){throw 'Release exceeds verification byte limit.'}
 $path=Join-Path $directory $asset.name
 Invoke-WebRequest -Uri $expected -OutFile $path -TimeoutSec 180
 $actual=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
 if(('sha256:'+$actual) -ne $asset.digest -or (Get-Item -LiteralPath $path).Length -ne $asset.size){throw ('Downloaded asset mismatch: '+$asset.name)}
 $downloaded[$asset.name]=$path
 $verified+=[ordered]@{name=$asset.name;sha256=$actual;bytes=$asset.size}
}
foreach($required in @('BUILD-INFO.json','SHA256SUMS.txt','SHA256SUMS-tools.txt','SHA256SUMS-core.txt')){if(!$downloaded.ContainsKey($required)){throw ('Missing '+$required)}}
$build=Get-Content -LiteralPath $downloaded['BUILD-INFO.json'] -Raw | ConvertFrom-Json
if($build.source -ne $source -or ('v'+$build.version) -ne $Tag){throw 'Build-info/tag/source mismatch.'}
$manifest=@{}
foreach($name in @('SHA256SUMS.txt','SHA256SUMS-tools.txt','SHA256SUMS-core.txt')){
 foreach($line in Get-Content -LiteralPath $downloaded[$name]){
  if(!$line.Trim()){continue}
  if($line -notmatch '^([a-fA-F0-9]{64})  ([A-Za-z0-9_.-]+)$'){throw 'Invalid manifest entry.'}
  $hash=$Matches[1].ToLowerInvariant();$file=$Matches[2]
  if($manifest.ContainsKey($file) -or !$downloaded.ContainsKey($file)){throw 'Ambiguous/unpublished manifest entry.'}
  if((Get-FileHash -LiteralPath $downloaded[$file] -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash){throw 'Manifest/actual-byte mismatch.'}
  $manifest[$file]=$hash
 }
}
$binaries=@($release.assets | Where-Object { $_.name -match '\.(zip|nupkg)$' })
if($manifest.Count -ne $binaries.Count -or @($binaries | Where-Object { !$manifest.ContainsKey($_.name) }).Count){throw 'Binary manifest coverage incomplete.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tools=@('FileOrganizer','DeviceInventory','WorkspaceLauncher','SystemMonitor','SystemCenter','TextEditor')
$binaryChecks=@()
foreach($asset in $binaries | Where-Object name -like '*.zip'){
 $zip=[IO.Compression.ZipFile]::OpenRead($downloaded[$asset.name])
 try{
  if($asset.name -eq 'Aquarium-complete-windows-x64.zip'){
   if($zip.Entries.Count -ne 1 -or $zip.Entries[0].FullName -ne 'Aquarium.exe'){throw 'Hub layout invalid.'}
   $tool='Aquarium';$entry=$zip.Entries[0];$expectedVersion=$Tag.Substring(1)
  }elseif($asset.name -match '^([A-Za-z]+)-windows-x64\.zip$' -and $tools -contains $Matches[1]){
   $tool=$Matches[1]
   foreach($file in @("$tool.exe","$tool.dll",'coreclr.dll')){if(!$zip.GetEntry($file)){throw ('Incomplete standalone runtime '+$tool)}}
   $entry=$zip.GetEntry("$tool.dll")
   $spec=$source+':tools/'+$tool+'/'+$tool+'.csproj'
   [xml]$project=git show $spec
   if($LASTEXITCODE -ne 0){throw 'Missing source tool project.'}
   $expectedVersion=[string]$project.Project.PropertyGroup.Version
  }else{throw 'Unexpected executable archive.'}
  # Extract only a known, already hash-verified PE resource for metadata inspection; never execute it.
  $file=Join-Path $directory ('metadata-'+$tool+'.bin')
  [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$file,$false)
  $version=[Diagnostics.FileVersionInfo]::GetVersionInfo($file).ProductVersion
  if(!$version -or $version.Split('+')[0] -ne $expectedVersion){throw ('Binary/source version mismatch: '+$tool)}
  $embeddedRevision=$null
  if($version.Contains('+')){$embeddedRevision=$version.Substring($version.IndexOf('+')+1);if($embeddedRevision -ne $source){throw 'Embedded source revision differs.'}}
  $binaryChecks+=[ordered]@{application=$tool;version=$version;embeddedRevision=$embeddedRevision;runtimeLayout='verified';executed=$false}
 }finally{$zip.Dispose()}
}
$report=[ordered]@{schemaVersion=1;recordedAtUtc=[DateTimeOffset]::UtcNow;application='Aquarium';tag=$Tag;source=$source;binaryManifestEntries=$manifest.Count;downloadedAssets=$verified;binaryChecks=$binaryChecks;publisherAuthentication='Not established by hash consistency; this is not signing or independent security certification.'}
$report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $directory 'verification.json') -Encoding utf8
Write-Output ("Verified public "+$Tag+": "+$verified.Count+" downloaded asset digests, "+$manifest.Count+" binary manifest entries and "+$binaryChecks.Count+" executable/runtime layouts. No downloaded program was executed.")
