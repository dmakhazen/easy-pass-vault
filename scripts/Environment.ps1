$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $projectRoot '.tools/nuget-http'
$env:TEMP = Join-Path $projectRoot '.tools/tmp'
$env:TMP = $env:TEMP
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $projectRoot '.tools/bundle-runtime'
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
$dotnetExe = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source }
