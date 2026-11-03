param([string]$OutputDirectory = '.artifacts/portable/win-x64')

. "$PSScriptRoot/Environment.ps1"
Push-Location $projectRoot
try {
    & $dotnetExe publish src/EasyPassVault.App/EasyPassVault.App.csproj -c Release --no-restore -m:1 -p:DebugType=embedded -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath vendor/KeePassLib/LICENSE -Destination (Join-Path $OutputDirectory 'COPYING.txt')
    Copy-Item -LiteralPath THIRD_PARTY_NOTICES.md -Destination (Join-Path $OutputDirectory 'THIRD_PARTY_NOTICES.md')
    # Old symbol files from previous local publishes are not needed: symbols are embedded.
    foreach ($symbolName in @('EasyPassVault.Core.pdb', 'KeePassLib.Standard.pdb')) {
        $symbolPath = Join-Path $OutputDirectory $symbolName
        if (Test-Path -LiteralPath $symbolPath) { Remove-Item -LiteralPath $symbolPath }
    }
} finally { Pop-Location }
