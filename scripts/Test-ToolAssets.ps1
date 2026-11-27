param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Directory)
$tools=@('FileOrganizer','DeviceInventory','WorkspaceLauncher','SystemMonitor','SystemCenter','TextEditor')
$manifestPath=Join-Path $root 'SHA256SUMS-tools.txt'
if((Get-Item -LiteralPath $manifestPath).Length -gt 32768){throw 'Tool manifest exceeds its limit.'}
$manifest=@(Get-Content -LiteralPath $manifestPath | Where-Object {$_.Trim()})
if($manifest.Count -ne $tools.Count){throw 'Tool manifest does not match the approved standalone tool set.'}
foreach($tool in $tools){
 $name="$tool-windows-x64.zip"
 $entry=@($manifest | Where-Object {$_ -cmatch ('^[a-f0-9]{64}  '+[regex]::Escape($name)+'$')})
 if($entry.Count -ne 1){throw "Missing, duplicate or invalid tool checksum: $name"}
 $file=Join-Path $root $name;$info=Get-Item -LiteralPath $file
 if($info.PSIsContainer -or ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $info.Length -gt 512MB){throw 'Unsupported tool asset.'}
 if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $entry[0].Substring(0,64)){throw "Tool checksum mismatch: $name"}
 $file
}
