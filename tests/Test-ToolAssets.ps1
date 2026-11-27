$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('Aquarium-tool-assets-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$validator=Join-Path $PSScriptRoot '../scripts/Test-ToolAssets.ps1'
$tools=@('FileOrganizer','DeviceInventory','WorkspaceLauncher','SystemMonitor','SystemCenter','TextEditor')
$manifestPath=Join-Path $root 'SHA256SUMS-tools.txt';$checks=0
function RestoreFixture {
 $lines=foreach($tool in $tools){
  $name="$tool-windows-x64.zip";$path=Join-Path $root $name
  [IO.File]::WriteAllText($path,"Inert generated $tool fixture")
  (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()+'  '+$name
 }
 $lines | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
}
function MustReject {
 $rejected=$false
 try{& $validator -Directory $root | Out-Null}catch{$rejected=$true}
 if(!$rejected){throw 'Malformed fixture was accepted.'}
 $script:checks++
}
try{
 RestoreFixture
 $valid=@(& $validator -Directory $root)
 if($valid.Count -ne $tools.Count -or @($valid | Where-Object {$_ -like '*TextEditor-windows-x64.zip'}).Count -ne 1){throw 'The complete six-tool fixture was not returned.'};$checks++
 $original=@(Get-Content -LiteralPath $manifestPath)
 $original | Select-Object -First 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM;MustReject
 RestoreFixture;$lines=@(Get-Content -LiteralPath $manifestPath);$lines[5]=$lines[0];$lines | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM;MustReject
 RestoreFixture;[IO.File]::AppendAllText((Join-Path $root 'TextEditor-windows-x64.zip'),'changed');MustReject
 RestoreFixture;$lines=@(Get-Content -LiteralPath $manifestPath);$lines[5]=$lines[5].Replace('TextEditor','UnknownTool');$lines | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM;MustReject
 RestoreFixture;[IO.File]::Delete((Join-Path $root 'TextEditor-windows-x64.zip'));MustReject
 Write-Output "$checks standalone-tool publication fixture checks passed. Only inert generated files were read."
}finally{
 $full=[IO.Path]::GetFullPath($root);$parent=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
 if(!$full.StartsWith($parent,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($full) -notmatch '^Aquarium-tool-assets-[a-f0-9]{32}$'){throw 'Unsafe fixture cleanup path.'}
 Remove-Item -LiteralPath $full -Recurse -Force
}
