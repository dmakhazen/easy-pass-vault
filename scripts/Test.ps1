. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnetExe build tests/EasyPassVault.Tests/EasyPassVault.Tests.csproj --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Integration test build failed.' }
    & $dotnetExe run --project tests/EasyPassVault.Tests/EasyPassVault.Tests.csproj --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Integration tests failed.' }
    & $dotnetExe build src/EasyPassVault.App/EasyPassVault.App.csproj --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
    & $dotnetExe build tests/EasyPassVault.UiTests/EasyPassVault.UiTests.csproj --no-restore -m:1
    if ($LASTEXITCODE -ne 0) { throw 'WPF test build failed.' }
    & $dotnetExe run --project tests/EasyPassVault.UiTests/EasyPassVault.UiTests.csproj --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'WPF tests failed.' }
} finally { Pop-Location }
