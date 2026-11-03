. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnetExe build tools/IconBuilder/IconBuilder.csproj --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Icon builder failed.' }
    & $dotnetExe run --project tools/IconBuilder/IconBuilder.csproj --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
} finally { Pop-Location }
