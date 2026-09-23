param(
    [string]$Apk = '.\app\build\outputs\apk\release\app-release.apk'
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Apk)) { throw "APK not found: $Apk" }

$BuildToolsRoot = Join-Path "$env:LOCALAPPDATA\Android\Sdk" 'build-tools'
$Latest = Get-ChildItem $BuildToolsRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $Latest) { throw 'Android build-tools not found.' }

$ApkSigner = Join-Path $Latest.FullName 'apksigner.bat'
$Aapt = Join-Path $Latest.FullName 'aapt.exe'
if (-not (Test-Path $ApkSigner)) { throw "apksigner not found: $ApkSigner" }
if (-not (Test-Path $Aapt)) { throw "aapt not found: $Aapt" }

Write-Host '--- Signature ---'
& $ApkSigner verify --verbose --print-certs $Apk
if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }

Write-Host '--- Permissions ---'
$Permissions = (& $Aapt dump permissions $Apk | Out-String)
$Permissions
if ($Permissions -match 'android\.permission\.INTERNET') {
    throw 'SECURITY FAILURE: INTERNET permission is present.'
}

Write-Host '--- Debuggable ---'
$Badging = (& $Aapt dump badging $Apk | Out-String)
if ($Badging -match 'application-debuggable') {
    throw 'SECURITY FAILURE: release APK is debuggable.'
}

Write-Host '--- Manifest flags ---'
$Manifest = (& $Aapt dump xmltree $Apk AndroidManifest.xml | Out-String)
$AllowBackup = ($Manifest -split "`n" | Where-Object { $_ -match 'android:allowBackup' } | Select-Object -First 1)
$Cleartext = ($Manifest -split "`n" | Where-Object { $_ -match 'android:usesCleartextTraffic' } | Select-Object -First 1)
if (-not $AllowBackup -or $AllowBackup -notmatch '0x0') {
    throw "SECURITY FAILURE: allowBackup=false not confirmed. Line: $AllowBackup"
}
if (-not $Cleartext -or $Cleartext -notmatch '0x0') {
    throw "SECURITY FAILURE: usesCleartextTraffic=false not confirmed. Line: $Cleartext"
}

Write-Host 'Security checks passed.'
Write-Host "SHA-256: $((Get-FileHash $Apk -Algorithm SHA256).Hash.ToLowerInvariant())"
