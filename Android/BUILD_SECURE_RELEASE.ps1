param(
    [Parameter(Mandatory=$true)][string]$Keystore,
    [string]$Alias = 'geniapassword-release'
)

$ErrorActionPreference = 'Stop'
$ExpectedWrapper = '7a9ce74cff467ca1bf60a4fcd9f05185acceda4d0f382434d393e17864262c5d'
$WrapperJar = '.\gradle\wrapper\gradle-wrapper.jar'
if (-not (Test-Path $WrapperJar)) { throw "Gradle wrapper JAR not found: $WrapperJar" }
$WrapperHash = (Get-FileHash $WrapperJar -Algorithm SHA256).Hash.ToLowerInvariant()
if ($WrapperHash -ne $ExpectedWrapper) {
    throw 'Gradle 9.7.0 wrapper is not verified. Run .\REFRESH_GRADLE_WRAPPER.ps1 first.'
}

if (-not (Test-Path $Keystore)) { throw "Keystore not found: $Keystore" }
if (-not (Test-Path '.\gradle\verification-metadata.xml')) {
    throw 'gradle/verification-metadata.xml is missing. Run .\PIN_DEPENDENCIES.ps1 first.'
}

$Sdk = "$env:LOCALAPPDATA\Android\Sdk"
"sdk.dir=$($Sdk -replace '\\','/')" | Set-Content -Encoding ASCII .\local.properties

$StoreSecure = Read-Host 'Keystore password' -AsSecureString
$KeySecure = Read-Host 'Key password' -AsSecureString
$StorePtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($StoreSecure)
$KeyPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($KeySecure)
try {
    $env:GENIAPASSWORD_KEYSTORE = (Resolve-Path $Keystore).Path
    $env:GENIAPASSWORD_KEYSTORE_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($StorePtr)
    $env:GENIAPASSWORD_KEY_ALIAS = $Alias
    $env:GENIAPASSWORD_KEY_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($KeyPtr)

    .\gradlew.bat clean test assembleRelease
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
} finally {
    $env:GENIAPASSWORD_KEYSTORE = $null
    $env:GENIAPASSWORD_KEYSTORE_PASSWORD = $null
    $env:GENIAPASSWORD_KEY_ALIAS = $null
    $env:GENIAPASSWORD_KEY_PASSWORD = $null
    if ($StorePtr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($StorePtr) }
    if ($KeyPtr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($KeyPtr) }
}

$Apk = '.\app\build\outputs\apk\release\app-release.apk'
if (-not (Test-Path $Apk)) { throw "Release APK not found: $Apk" }
Write-Host "Release APK: $Apk"
Write-Host "SHA-256: $((Get-FileHash $Apk -Algorithm SHA256).Hash.ToLowerInvariant())"
