# GeniaPassword

**GeniaPassword** is a local, offline password manager for Windows and Android.

The project follows one simple rule: **password data stays on the user's device**.

## Platforms

- **Windows** — GeniaPassword 2.1.1
- **Android** — GeniaPassword Mobile 1.0

This repository contains the regular desktop and mobile editions. PRO editions are maintained separately.

## Security model

GeniaPassword is designed to work without cloud services, accounts, telemetry, analytics, advertising SDKs, or background network services.

Core properties:

- offline-only password management;
- Vault v2 encrypted storage;
- Argon2id password-based key derivation;
- AES-256-GCM authenticated encryption;
- local backup/import/export;
- no password recovery service;
- Android build does not request the `INTERNET` permission;
- the user is responsible for keeping a safe backup and remembering the master password.

> No software can guarantee absolute security. Review the source, keep backups, use a strong master password, and protect the operating system itself.

## Source tree

- [Windows/](Windows/) — regular Windows source, including the explicit password-field **Paste** button.
- [Android/](Android/) — regular Android source.

Published source intentionally excludes user vaults, signing keys, release keystores, build output, and machine-local SDK configuration.

## Build

See [docs/BUILD.md](docs/BUILD.md) for detailed instructions.

### Windows

```powershell
cd .\Windows
.\BUILD_PORTABLE.cmd
```

### Android

```powershell
cd .\Android
.\gradlew.bat clean test assembleDebug
```

Debug APK:

```text
Android\app\build\outputs\apk\debug\app-debug.apk
```

## Vault compatibility

Windows and Android regular editions use the same Vault v2 cryptographic container and are intended for file-based interoperability.

Keep a backup before moving or importing a vault between devices. See [docs/VAULT_COMPATIBILITY.md](docs/VAULT_COMPATIBILITY.md).

## Publication safety review

The source tree was statically checked before publication for accidentally included vaults, signing material, build output, common token/key patterns, and network regressions.

See [docs/PUBLICATION_AUDIT_2026-09-23.md](docs/PUBLICATION_AUDIT_2026-09-23.md).

## Project principles

- No cloud synchronization.
- No external password APIs.
- No telemetry or advertising SDKs.
- No hidden network features.
- Security-sensitive changes require extra review.
- Vault files, backups, signing keys, and local machine configuration must never be committed.

## Contributing and security reports

- [CONTRIBUTING.md](CONTRIBUTING.md)
- [SECURITY.md](SECURITY.md)

Do not post real vault files, master passwords, signing keys, or exploitable vulnerability details in public issues.

## License

GeniaPassword is released under the **GNU General Public License v3.0 or later (GPL-3.0-or-later)**.

See [LICENSE](LICENSE) for the full license text.

## Repository

Maintained by **GeniaSoft**.

Project page: https://github.com/geniasoftwin/Genia-Password
