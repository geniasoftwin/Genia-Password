# Public release safety audit — 2026-09-23

This document records the static publication checks performed before publishing the regular Windows and Android source trees.

## Scope

- Windows: GeniaPassword 2.1.1, including the explicit password-field Paste button UI fix.
- Android: GeniaPassword Mobile 1.0 Final.
- PRO editions are not included in this repository.

## Publication hygiene

Checked before publication:

- no `vault.pnb`, vault backups, temporary vault files, APK, AAB, EXE or DLL build outputs;
- no Android signing keystore, PFX/P12/PEM/private-key material;
- no `local.properties` (only `local.properties.example`);
- no build output directories such as `bin`, `obj`, `build`, `.gradle`, `dist`;
- no GitHub tokens, Google API keys, AWS-style keys, or embedded private keys found by static pattern scan;
- Android test passwords are fixed disposable test vectors only and are not user credentials.

## Offline invariants

Android manifest:

- does not request `android.permission.INTERNET`;
- `android:allowBackup="false"`;
- `android:usesCleartextTraffic="false"`;
- backup/data-extraction rules exclude application data.

Windows static source scan found no `HttpClient`, `WebClient`, `WebRequest`, `TcpClient`, `UdpClient` or socket-based networking implementation. URL text in the Windows source is used for local website-field checks/document metadata, not network access.

## Cryptography and third-party code

The published code retains the project's Vault v2 design and the existing cryptographic implementations used by the tested builds.

Third-party notices are included:

- Windows: TopSecret.Cryptography.Argon2 2.2.0 (MIT).
- Android: Bouncy Castle `bcprov-jdk18on` 1.85.2 (MIT-style Bouncy Castle license).

The repository itself is licensed under GPL-3.0-or-later.

## Notes

This was a static publication/security-hygiene review, not an independent cryptographic audit or formal proof of security. Release binaries should still be built on a trusted machine and smoke-tested before distribution.
