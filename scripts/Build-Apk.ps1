# Builds the Android viewer APK. Everything it needs lives inside the project:
#   .tools/dotnet            .NET SDK with the "android" workload
#   .tools/android/jdk       JDK
#   .tools/android/sdk       Android SDK (installed by: dotnet build -t:InstallAndroidDependencies)
#   .tools/android/signing   the key that signs the APK; created on the first run, never committed
# -AllowScreenshots builds a test APK without the screenshot ban, for checking the screens over adb.
param(
    [string]$OutputDirectory = '.artifacts/android',
    [switch]$AllowScreenshots
)

. "$PSScriptRoot/Environment.ps1"
$androidRoot = Join-Path $projectRoot '.tools/android'
$sdk = Join-Path $androidRoot 'sdk'
$jdkRoot = Join-Path $androidRoot 'jdk'
if (!(Test-Path -LiteralPath $sdk) -or !(Test-Path -LiteralPath $jdkRoot)) { throw 'Android SDK or JDK is missing in .tools/android.' }
$jdk = (Get-ChildItem -LiteralPath $jdkRoot -Directory | Select-Object -First 1).FullName

# Android updates an installed app only when the new APK is signed with the same key.
# Losing this key means: uninstall the app once, then install the new build. The vault file is not affected.
$signing = Join-Path $androidRoot 'signing'
$keystore = Join-Path $signing 'easy-pass-vault.p12'
$secret = Join-Path $signing 'store-pass.txt'
if (!(Test-Path -LiteralPath $keystore)) {
    New-Item -ItemType Directory -Path $signing -Force | Out-Null
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $pass = [Convert]::ToBase64String($bytes).Replace('+', 'A').Replace('/', 'B').Replace('=', '')
    # Two equal lines: apksigner reads the store password and the key password from consecutive lines of one file.
    [System.IO.File]::WriteAllText($secret, "$pass`n$pass`n", [System.Text.Encoding]::ASCII)
    # keytool reports progress on stderr; with 'Stop' Windows PowerShell would treat that as a failure.
    $ErrorActionPreference = 'Continue'
    & (Join-Path $jdk 'bin/keytool.exe') -genkeypair -keystore $keystore -storetype PKCS12 -alias epv -keyalg RSA -keysize 3072 `
        -validity 10000 -dname 'CN=easy-pass-vault' "-storepass:file" $secret "-keypass:file" $secret 2>$null
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $keystore)) { throw 'Could not create the signing key.' }
}

Push-Location $projectRoot
try {
    $project = 'src/EasyPassVault.Android/EasyPassVault.Android.csproj'
    $properties = @(
        "-p:AndroidSdkDirectory=$sdk", "-p:JavaSdkDirectory=$jdk",
        '-p:AndroidKeyStore=true', "-p:AndroidSigningKeyStore=$keystore", '-p:AndroidSigningKeyAlias=epv',
        "-p:AndroidSigningKeyPass=file:$secret", "-p:AndroidSigningStorePass=file:$secret",
        "-p:AllowScreenshots=$($AllowScreenshots.IsPresent)")
    & $dotnetExe restore $project --configfile NuGet.Config --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Package restore failed.' }
    $built = 'src/EasyPassVault.Android/bin/Release/net10.0-android/app.easypassvault-Signed.apk'
    # A leftover from a build whose signing step failed would otherwise be taken for a fresh APK.
    if (Test-Path -LiteralPath $built) { Remove-Item -LiteralPath $built }
    & $dotnetExe build $project -c Release --no-restore -m:1 @properties
    if ($LASTEXITCODE -ne 0) { throw 'APK build failed.' }

    $version = [version](Select-Xml -Path src/EasyPassVault.App/EasyPassVault.App.csproj -XPath '//Version').Node.InnerText
    $name = 'easy-pass-vault-{0}.{1:D2}{2}.apk' -f $version.Major, $version.Minor, $(if ($AllowScreenshots) { '-test' } else { '' })
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $apk = Join-Path $OutputDirectory $name
    $apksigner = (Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Recurse -Filter apksigner.jar | Sort-Object FullName | Select-Object -Last 1).FullName
    $ErrorActionPreference = 'Continue'
    & (Join-Path $jdk 'bin/java.exe') -jar $apksigner verify --min-sdk-version 26 $built 2>$null | Out-Null
    $verified = $LASTEXITCODE -eq 0
    $ErrorActionPreference = 'Stop'
    if (!$verified) { throw 'The built APK is not signed correctly.' }
    Copy-Item -LiteralPath $built -Destination $apk -Force
    Write-Output "APK: $apk"
} finally { Pop-Location }
