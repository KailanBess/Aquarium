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
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $PublishDirectory 'README.md')
$archive=Join-Path $OutputDirectory 'Aquarium-complete-windows-x64.zip'
Compress-Archive -Path (Join-Path $PublishDirectory '*') -DestinationPath $archive
((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()+'  Aquarium-complete-windows-x64.zip') | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
@{application='Aquarium';version=$version;source=$env:GITHUB_SHA;bundled=$bundled;signed=$false;fishBowl='Optional public release install; no games, firmware or emulators included'} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputDirectory 'BUILD-INFO.json') -Encoding utf8
