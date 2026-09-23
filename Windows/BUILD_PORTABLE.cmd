@echo off
setlocal EnableExtensions

rem Work from the folder where this script is located.
pushd "%~dp0" >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Cannot open the source folder.
  pause
  exit /b 1
)

set "PROJECT=PasswordNotebook\GeniaPassword.csproj"
set "PUBLISH_DIR=PasswordNotebook\bin\Release\net10.0-windows\win-x64\publish"
set "DIST_DIR=dist\GeniaPassword-Portable"
set "PUBLISHED_EXE=%PUBLISH_DIR%\GeniaPassword.exe"
set "DIST_EXE=%DIST_DIR%\GeniaPassword.exe"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERROR] .NET SDK 10 not found.
  echo Install Visual Studio 2026 with .NET desktop development or .NET 10 SDK.
  popd
  pause
  exit /b 1
)

if not exist "%PROJECT%" (
  echo [ERROR] Project not found: "%PROJECT%"
  popd
  pause
  exit /b 1
)

rem Do not use dotnet publish -o here.
rem MSBuild can misparse a fully resolved PublishDir when a parent folder
rem contains characters such as a comma. Publishing to the SDK default path
rem avoids that command-line property parsing issue.
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "%DIST_DIR%" rmdir /s /q "%DIST_DIR%"

mkdir "%DIST_DIR%" >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Cannot create: "%DIST_DIR%"
  popd
  pause
  exit /b 1
)

echo Publishing GeniaPassword portable win-x64 single-file...
dotnet publish "%PROJECT%" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  --nologo

if errorlevel 1 (
  echo.
  echo [ERROR] Publish failed.
  popd
  pause
  exit /b 1
)

if not exist "%PUBLISHED_EXE%" (
  echo.
  echo [ERROR] GeniaPassword.exe was not produced at:
  echo "%CD%\%PUBLISHED_EXE%"
  popd
  pause
  exit /b 1
)

copy /y "%PUBLISHED_EXE%" "%DIST_EXE%" >nul
if errorlevel 1 (
  echo.
  echo [ERROR] Cannot copy GeniaPassword.exe to the portable folder.
  popd
  pause
  exit /b 1
)

for /f %%C in ('dir /b /a-d "%DIST_DIR%" ^| find /c /v ""') do set "COUNT=%%C"
if not "%COUNT%"=="1" (
  echo.
  echo [ERROR] Expected exactly one portable file, found %COUNT%.
  dir /b "%DIST_DIR%"
  popd
  pause
  exit /b 1
)

if not exist "%DIST_EXE%" (
  echo.
  echo [ERROR] Final GeniaPassword.exe is missing.
  popd
  pause
  exit /b 1
)

echo.
echo [OK] Portable build created:
echo "%CD%\%DIST_EXE%"
echo.
echo On first launch the app will create next to the EXE:
echo   vault.pnb
echo   vault.pnb.bak
echo   backups\   ^(up to 10 encrypted restore points after subsequent saves^)

popd
pause
exit /b 0
