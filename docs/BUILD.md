# Building GeniaPassword

## Windows

Requirements:

- Windows 10/11
- .NET 10 SDK with Windows Desktop support

From the repository root:

```powershell
cd .\Windows
.\BUILD_PORTABLE.cmd
```

The project targets Windows x64 and publishes a portable self-contained single-file build.

## Android

Requirements:

- Android SDK
- compatible JDK
- Gradle wrapper included in the repository

From the repository root:

```powershell
cd .\Android
.\gradlew.bat clean test assembleDebug
```

Debug APK:

```text
Android\app\build\outputs\apk\debug\app-debug.apk
```

The regular Android source keeps release signing credentials outside the repository. Never commit a permanent signing keystore, its passwords, or `local.properties`.

## Release hygiene

Before publishing a binary build:

1. verify that no vault files or backups are present;
2. verify that no signing keys or local SDK paths are included;
3. run the available tests and static checks;
4. verify Android still does not request the `INTERNET` permission;
5. calculate SHA-256 for release binaries;
6. test opening, editing, saving, locking and reopening a disposable test vault;
7. test Windows ↔ Android Vault v2 interchange using a disposable test vault.
