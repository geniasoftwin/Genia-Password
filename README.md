# GeniaPassword

**GeniaPassword** is a local, offline password manager for Windows and Android.

The project is built around a simple rule: **password data stays on the user's device**.

## Platforms

- **Windows** — GeniaPassword 2.1.1
- **Android** — GeniaPassword Mobile 1.0

The regular desktop and mobile editions are published here. PRO editions are maintained separately.

## Security model

GeniaPassword is designed to work without cloud services, accounts, telemetry, analytics, or background network services.

Core properties:

- offline-first / offline-only operation;
- Vault v2 encrypted storage;
- Argon2id password-based key derivation;
- AES-256-GCM authenticated encryption;
- local backup/import/export;
- no password recovery service;
- Android build does not request the `INTERNET` permission;
- user is responsible for keeping a safe backup of the vault and remembering the master password.

> No software can guarantee absolute security. Review the source, keep backups, use a strong master password, and protect the operating system itself.

## Source code

This repository is for the regular editions of GeniaPassword:

- Windows source
- Android source

Published source must never contain user vaults, signing keys, release keystores, build output, or local SDK configuration.

## Build

### Windows

Requirements:

- Windows
- .NET 10 SDK with Windows Desktop support

Extract the Windows source package and run:

```powershell
.\BUILD_PORTABLE.cmd
```

### Android

Requirements:

- Android SDK
- JDK / Gradle environment matching the project configuration

Extract the Android source package and run:

```powershell
.\gradlew.bat clean test assembleDebug
```

The debug APK is produced under:

```text
app\build\outputs\apk\debug\app-debug.apk
```

## Vault compatibility

Windows and Android regular editions use the same Vault v2 cryptographic container and are intended for file-based interoperability.

For safety, keep a backup before moving or importing a vault between devices.

## Project principles

- No cloud synchronization.
- No external password APIs.
- No telemetry or advertising SDKs.
- No hidden network features.
- Security-sensitive changes should be reviewed before release.
- Vault files, backups, signing keys, and local machine configuration must never be committed.

## Security reports

Please read [SECURITY.md](SECURITY.md) before reporting a vulnerability.

## License

GeniaPassword is released under the **GNU General Public License v3.0 or later (GPL-3.0-or-later)**.

See [LICENSE](LICENSE) for the full license text.

## Repository

Maintained by **GeniaSoft**.

Project page: https://github.com/geniasoftwin/Genia-Password
