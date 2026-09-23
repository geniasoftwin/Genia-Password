$ErrorActionPreference = 'Stop'
$ExpectedWrapper = '7a9ce74cff467ca1bf60a4fcd9f05185acceda4d0f382434d393e17864262c5d'
$WrapperJar = '.\gradle\wrapper\gradle-wrapper.jar'
if (-not (Test-Path $WrapperJar)) { throw "Gradle wrapper JAR not found: $WrapperJar" }
$WrapperHash = (Get-FileHash $WrapperJar -Algorithm SHA256).Hash.ToLowerInvariant()
if ($WrapperHash -ne $ExpectedWrapper) {
    throw 'Gradle 9.7.0 wrapper is not verified. Run .\REFRESH_GRADLE_WRAPPER.ps1 first.'
}


Write-Host 'Generating Gradle SHA-256 dependency verification metadata...'
Write-Host 'Run this only on a trusted network/machine and review gradle/verification-metadata.xml afterwards.'

$Sdk = "$env:LOCALAPPDATA\Android\Sdk"
"sdk.dir=$($Sdk -replace '\\','/')" | Set-Content -Encoding ASCII .\local.properties

.\gradlew.bat --write-verification-metadata sha256 assembleDebug test

$Metadata = '.\gradle\verification-metadata.xml'
if (-not (Test-Path $Metadata)) {
    throw 'Gradle did not create gradle/verification-metadata.xml'
}

Write-Host "Created $Metadata"
Write-Host 'Commit/archive this file together with the source after reviewing it.'
