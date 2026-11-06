$ErrorActionPreference='Stop'
$archive=Join-Path $PWD 'artifacts/release/Aquarium-complete-windows-x64.zip'
$test=Join-Path $env:RUNNER_TEMP ('aquarium-single-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $test | Out-Null
try {
 Add-Type -AssemblyName System.IO.Compression.FileSystem
 $zip=[IO.Compression.ZipFile]::OpenRead($archive)
 try {if($zip.Entries.Count -ne 1 -or $zip.Entries[0].FullName -ne 'Aquarium.exe'){throw 'The extracted download must contain only Aquarium.exe.'}} finally {$zip.Dispose()}
 Expand-Archive -LiteralPath $archive -DestinationPath $test
 foreach($attempt in 1..3){
  if($attempt -eq 3){$moved=$test+'-moved';Move-Item $test $moved;$test=$moved}
  $process=Start-Process (Join-Path $test 'Aquarium.exe') -ArgumentList '--package-check' -PassThru
  try {
   if(!$process.WaitForExit(180000)){$process.Kill($true);$process.WaitForExit(5000)|Out-Null;throw "Launcher startup timed out on attempt $attempt."}
   if($process.ExitCode -ne 0){throw "Native Aquarium initialization failed on attempt $attempt with exit $($process.ExitCode)."}
  } finally {$process.Dispose()}
  if(@(Get-ChildItem $test -Force).Count -ne 1){throw 'Launcher left support files beside the app.'}
 }
 Write-Host 'PASS: one visible app, cold/repeat/moved native launches, XAML initialization, runtime PRI and both included app hashes.'
} catch {
 $message=$_.Exception.Message.Replace('%','%25').Replace("`r",'%0D').Replace("`n",'%0A')
 Write-Host "::error title=Single-app startup check::$message"
 throw
} finally {
 if(Test-Path -LiteralPath $test){
  $full=[IO.Path]::GetFullPath($test)
  $temp=[IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($env:RUNNER_TEMP))
  if([IO.Path]::GetDirectoryName($full) -ne $temp -or
      [IO.Path]::GetFileName($full) -notmatch '^aquarium-single-[a-f0-9]{32}(-moved)?$'){throw 'Unexpected smoke-test cleanup path.'}
  for($attempt=1;$attempt -le 10;$attempt++){
   try {Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop;break}
   catch [IO.IOException] {if($attempt -eq 10){throw};Start-Sleep -Seconds 1}
  }
 }
}

