param(
    [Parameter(Mandatory=$true)][string]$Output,
    [string]$Alias = 'geniapassword-release'
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path '.').Path.TrimEnd('\')
$FullOutput = [System.IO.Path]::GetFullPath($Output)
if ($FullOutput.StartsWith($ProjectRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'For safety, keep the permanent release keystore OUTSIDE the project directory.'
}

$Dir = Split-Path -Parent $FullOutput
if ($Dir -and -not (Test-Path $Dir)) { New-Item -ItemType Directory -Path $Dir | Out-Null }
if (Test-Path $FullOutput) { throw "Keystore already exists: $FullOutput" }

Write-Host 'A permanent release signing key will be created OUTSIDE the project.'
Write-Host 'IMPORTANT: keep at least two OFFLINE backups. Losing this key prevents seamless updates.'
Write-Host 'keytool will ask you for a strong keystore/key password.'

& keytool -genkeypair `
    -v `
    -keystore $FullOutput `
    -alias $Alias `
    -keyalg RSA `
    -keysize 4096 `
    -sigalg SHA256withRSA `
    -validity 10000

if ($LASTEXITCODE -ne 0) { throw 'keytool failed.' }
Write-Host "Created: $FullOutput"
Write-Host 'Do NOT place this keystore in the project ZIP, cloud sync, or source control.'
