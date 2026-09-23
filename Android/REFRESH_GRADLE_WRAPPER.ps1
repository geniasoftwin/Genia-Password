$ErrorActionPreference = 'Stop'
$ExpectedTargetJar = '7a9ce74cff467ca1bf60a4fcd9f05185acceda4d0f382434d393e17864262c5d'
$ExpectedBootstrapJar = '76805e32c009c0cf0dd5d206bddc9fb22ea42e84db904b764f3047de095493f3'
$ExpectedDist = '84fbba45c7f4c64abc77460e1c00f541e9f960e3c7ed2538f1ede19eacd873ae'
$Jar = '.\gradle\wrapper\gradle-wrapper.jar'

if (-not (Test-Path $Jar)) { throw "Gradle wrapper JAR not found: $Jar" }
$Before = (Get-FileHash $Jar -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Before -ne $ExpectedBootstrapJar -and $Before -ne $ExpectedTargetJar) {
    throw "Refusing to execute an unrecognized Gradle wrapper JAR: $Before"
}

.\gradlew.bat wrapper `
    --gradle-version 9.7.0 `
    --distribution-type bin `
    --gradle-distribution-sha256-sum $ExpectedDist
if ($LASTEXITCODE -ne 0) { throw 'Gradle wrapper refresh failed.' }

$Actual = (Get-FileHash $Jar -Algorithm SHA256).Hash.ToLowerInvariant()
if ($Actual -ne $ExpectedTargetJar) {
    throw "Unexpected Gradle 9.7.0 wrapper JAR SHA-256: $Actual (expected $ExpectedTargetJar)"
}
Write-Host 'Gradle 9.7.0 wrapper verified.'
