param([Parameter(Mandatory)][string]$PublishDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$Tag)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
[xml]$project=Get-Content (Join-Path $root 'src/Aquarium/Aquarium.csproj')
$version=[string]$project.Project.PropertyGroup.Version
if($Tag -ne 'v'+$version -or $Tag -notmatch '^v\d+\.\d+\.\d+$'){throw 'Release tag/source version mismatch.'}
foreach($name in @('Aquarium.exe','Aquarium.dll','Aquarium.deps.json','Aquarium.runtimeconfig.json','Data/catalog.json','Bundled/bundled.json')){if(!(Test-Path -LiteralPath (Join-Path $PublishDirectory $name))){throw "Incomplete Aquarium runtime: $name"}}
$identity=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $PublishDirectory 'Aquarium.dll'))
if($identity.Name -ne 'Aquarium' -or $identity.Version -ne [Version]($version+'.0')){throw 'Aquarium assembly version mismatch.'}
if(@(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){throw 'Publish folder contains a link.'}
$bundled=@(Get-Content (Join-Path $PublishDirectory 'Bundled/bundled.json') -Raw | ConvertFrom-Json)
if($bundled.Count -ne 2 -or @($bundled.appId | Sort-Object -Unique).Count -ne 2){throw 'Unexpected included-app manifest.'}
foreach($item in $bundled){
 if($item.file -notin @('FishTank.zip','FishNet.zip') -or $item.appId -notin @('aquarium.security','aquarium.media')){throw 'Unapproved included app.'}
 if((Get-FileHash (Join-Path $PublishDirectory ('Bundled/'+$item.file)) -Algorithm SHA256).Hash -ne $item.sha256){throw 'Included app checksum mismatch.'}
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
# The distributable contains only the launcher; the complete verified WinUI runtime
# is embedded and unpacked into the current user's versioned runtime cache.
$PublishDirectory=[IO.Path]::GetFullPath($PublishDirectory)
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$work=Join-Path $OutputDirectory 'launcher-work'
$visible=Join-Path (Split-Path -Parent $OutputDirectory) 'Aquarium-single-app'
New-Item -ItemType Directory -Path $work,$visible -Force | Out-Null
if(@(Get-ChildItem $visible -Force).Count){throw 'Launcher output must be empty.'}
$payload=Join-Path $work 'runtime.zip'
$manifestPath=Join-Path $work 'runtime.json'
$files=@(Get-ChildItem $PublishDirectory -Recurse -File | Sort-Object FullName | ForEach-Object {
 @{Path=[IO.Path]::GetRelativePath($PublishDirectory,$_.FullName).Replace('\','/');Size=$_.Length;Sha256=(Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
})
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadZip=[IO.Compression.ZipFile]::Open($payload,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($file in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($payloadZip,(Join-Path $PublishDirectory $file.Path),$file.Path,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}
} finally {$payloadZip.Dispose()}
@{Version=$version;Sha256=(Get-FileHash $payload -Algorithm SHA256).Hash.ToLowerInvariant();Files=$files} | ConvertTo-Json -Depth 6 | Set-Content $manifestPath -Encoding utf8NoBOM
dotnet publish (Join-Path $root 'src/Aquarium.Launcher/Aquarium.Launcher.csproj') -c Release -o $visible "-p:PayloadPath=$payload" "-p:PayloadManifestPath=$manifestPath"
if($LASTEXITCODE -ne 0){throw 'Single-app launcher build failed.'}
if(@(Get-ChildItem $visible -Force).Count -ne 1 -or !(Test-Path (Join-Path $visible 'Aquarium.exe'))){throw 'Only Aquarium.exe may be visible in the download.'}
$archive=Join-Path $OutputDirectory 'Aquarium-complete-windows-x64.zip'
Compress-Archive -LiteralPath (Join-Path $visible 'Aquarium.exe') -DestinationPath $archive
Remove-Item $work -Recurse -Force
((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()+'  Aquarium-complete-windows-x64.zip') | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
@{application='Aquarium';version=$version;source=$env:GITHUB_SHA;bundled=$bundled;signed=$false;layout='single launcher; runtime cached under LocalAppData/Aquarium/Runtime';fishBowl='Optional public release install; no games, firmware or emulators included'} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory 'BUILD-INFO.json') -Encoding utf8
