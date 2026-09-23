# Building GeniaPassword

## Windows

Requirements:

- Windows 10/11
- .NET 10 SDK with Windows Desktop support

Extract `source/GeniaPassword_Windows_2_1_1_Source.zip`, open PowerShell in the extracted directory, then run:

```powershell
.\BUILD_PORTABLE.cmd
```

The project uses the Windows desktop target and produces a portable build according to the included publish configuration.

## Android

Requirements:

- Android SDK
- compatible JDK
- Gradle wrapper included in the source package

Extract `source/GeniaPasswordMobile_1_0_Source.zip` and run:

```powershell
.\gradlew.bat clean test assembleDebug
```

Debug APK:

```text
app\build\outputs\apk\debug\app-debug.apk
```

For a release build, keep the permanent signing keystore **outside** the project directory and never commit it.

## Release hygiene

Before publishing a build:

1. verify that no vault files or backups are present;
2. verify that no signing keys or local SDK paths are included;
3. run the available tests/static checks;
4. verify Android still does not request the `INTERNET` permission;
5. calculate SHA-256 for release binaries;
6. test opening, editing, saving, locking and reopening a test vault;
7. test Windows ↔ Android Vault v2 interchange using a disposable test vault.
