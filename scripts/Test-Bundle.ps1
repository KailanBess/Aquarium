param([Parameter(Mandatory)][string]$FishNetDirectory,[Parameter(Mandatory)][string]$FishTankArchive)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temp=Join-Path ([IO.Path]::GetTempPath()) ('Aquarium-bundle-test-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
 & (Join-Path $PSScriptRoot 'Bundle-Apps.ps1') -PublishDirectory (Join-Path $temp 'valid') -FishNetDirectory $FishNetDirectory -FishTankArchive $FishTankArchive | Out-Null
 $manifest=@(Get-Content (Join-Path $temp 'valid/Bundled/bundled.json') -Raw | ConvertFrom-Json)
 if($manifest.Count -ne 2 -or $manifest.version -notcontains 'v0.3.1' -or $manifest.version -notcontains 'preview-v0.16.0'){throw 'Included manifest identities are incorrect.'}
 $checks=1
 foreach($item in $manifest){if((Get-FileHash (Join-Path $temp ('valid/Bundled/'+$item.file)) -Algorithm SHA256).Hash -ne $item.sha256){throw 'Included package hash differs.'};$checks++}
 $rejected=$false
 try{& (Join-Path $PSScriptRoot 'Bundle-Apps.ps1') -PublishDirectory (Join-Path $temp 'wrong') -FishNetDirectory $FishNetDirectory -FishTankArchive $FishTankArchive -FishNetVersion 99.0.0 | Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Mismatched companion version accepted.'};$checks++
 $bad=Join-Path $temp 'bad.zip';Copy-Item -LiteralPath $FishTankArchive -Destination $bad
 $zip=[IO.Compression.ZipFile]::Open($bad,[IO.Compression.ZipArchiveMode]::Update)
 try{$zip.GetEntry('README.txt').Delete();$entry=$zip.CreateEntry('README.txt');$writer=New-Object IO.StreamWriter($entry.Open());try{$writer.Write('inert changed fixture')}finally{$writer.Dispose()}}finally{$zip.Dispose()}
 $rejected=$false
 try{& (Join-Path $PSScriptRoot 'Bundle-Apps.ps1') -PublishDirectory (Join-Path $temp 'bad') -FishNetDirectory $FishNetDirectory -FishTankArchive $bad | Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Changed package content accepted.'};$checks++
 Write-Host "$checks companion-bundle identity/digest checks passed. No app was installed or launched."
} finally {
 $full=[IO.Path]::GetFullPath($temp);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
 if(!$full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notmatch '^Aquarium-bundle-test-[0-9a-f]{32}$'){throw 'Unsafe bundle fixture cleanup path.'}
 Remove-Item -LiteralPath $full -Recurse -Force
}
