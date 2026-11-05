param([Parameter(Mandatory)][string]$Project,[Parameter(Mandatory)][string]$PublishDirectory,[Parameter(Mandatory)][string]$LogName)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Path artifacts -Force | Out-Null
$log=Join-Path 'artifacts' ($LogName+'.log')
$target=[IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
try {
 & msbuild $Project /restore /t:Publish /p:Configuration=Release /p:Platform=x64 "/p:PublishDir=$target" 2>&1 | Tee-Object $log
 if($LASTEXITCODE -ne 0){throw "Windows publish failed for $Project (exit $LASTEXITCODE)."}
} catch {
 $detail=$_.Exception.Message
 if(Test-Path $log){$detail+=[Environment]::NewLine+((Get-Content $log -Tail 60 | Where-Object {$_ -match '(?i)error|exception|failed'}) -join [Environment]::NewLine)}
 $escaped=$detail.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
 Write-Host "::error title=Windows release diagnostics::$escaped"
 if($env:GITHUB_STEP_SUMMARY){"## Windows release build failed`n$detail" | Add-Content $env:GITHUB_STEP_SUMMARY}
 throw
}
