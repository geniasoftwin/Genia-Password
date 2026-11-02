# GeniaPassword PRO — SmartMerge 2 WorkTest status (2026-11-02)

> **Development / WorkTest only.** The PRO projects are maintained separately from the regular GeniaPassword code published in this repository. No PRO source or executable is published here by this document.

## Current source packages

| Edition | WorkTest source package | Archive SHA-256 |
| --- | --- | --- |
| Windows PRO 1.2 | `GeniaPassword_PRO_1_2_SmartMerge2_BackupVerification_WorkTest_2026-11-01.zip` | `ab9aa1255c768aafca4a6975abbf5be73ebe4019c88cfccb8e9131c3e5cd3789` |
| Mobile PRO 1.1 | `GeniaPasswordMobile_PRO_1_1_SmartMerge2_UnitTestsFix_2026-11-01.zip` | `570578b723136a11c34da74fc498d3a97e5af170ce60a765ecb71c2a12ba0dce` |

These are source-archive identifiers from the development conversation, **not GitHub release assets or download links**. They should not be represented as publicly released builds.

The two ZIP files were readable with no corrupt archive members or path traversal entries. Their filenames were checked for vaults, keystores, APK/EXE builds and typical secret-bearing artifacts. This is a limited packaging check, **not** an exhaustive source security audit.

## Latest changes in development

### SmartMerge 2 — Windows / Android

- Per-conflict summaries list which parts of a record differ, rather than merely saying that a conflict exists.
- Summaries cover password equality, notes, favorites, categories, password history, account identity and added/changed custom fields as applicable to each platform.
- Secret values are not displayed in the preview.
- Existing explicit merge choices are preserved: keep current, accept imported, preserve both, or add only missing custom fields.
- Adding missing fields must not replace the local main password or existing fields.
- A no-op merge should leave the active vault unmodified. These properties still need end-to-end confirmation on both devices.

### Encrypted backup verification

- Windows PRO 1.2: the backup actions dialog can select and authenticate/decrypt a `.pnb` for read-only verification.
- Mobile PRO 1.1: the backups screen can verify an internal encrypted backup, using its master password, without restoring it.
- Successful verification should report non-secret details such as entry/category counts; a wrong password, corrupt file or truncated file should fail without altering the active vault.

The development approach preserves the existing offline-only architecture and Vault v2, Argon2id, AES-256-GCM design. **Independent security verification has not been completed.**

## Build and test status

### Windows PRO 1.2

The earlier SmartMerge MissingFields Windows build failed on C# 14 `field` identifier handling and a missing `WindowSecurity` namespace import. A subsequent BuildFix source archive addressed those two compilation errors. The current SmartMerge 2 source package builds on that corrected line, but **successful compilation of this newest ZIP is not yet evidenced here**.

Test locally on Windows with .NET 10:

```powershell
.\BUILD_PORTABLE.cmd
```

### Mobile PRO 1.1

The first SmartMerge 2 Android test run failed with **7 failed tests out of 14**, each reporting `VaultException`. Investigation found invalid non-UUID fixture IDs and a missing real JVM `org.json` test dependency. The updated source package uses UUID fixtures and adds `testImplementation("org.json:json:20240303")`. **A successful Gradle rerun of this exact updated ZIP has not yet been reported. Do not mark the regression resolved until it passes.**

Test locally in the Android project directory:

```powershell
.\gradlew.bat clean test assembleDebug
```

Expected debug output (only after a successful build): `app\build\outputs\apk\debug\app-debug.apk`.

## Required acceptance checks before publishing PRO

1. Windows desktop build completes, and Android unit tests and APK build finish with `BUILD SUCCESSFUL`.
2. Use **disposable** `.pnb` files and test Windows → Android → Windows round-trips, including a newly added secret custom field such as "Старый пароль".
3. Exercise each conflict choice, including unchanged main passwords, changed notes, category matches by name, repeated imports and cancel/no-op paths. Validate there is no unapproved replacement and no duplicate secret fields.
4. Check backup verification for valid, wrong-password, truncated and damaged `.pnb` cases. Verify the working vault hash and revision remain unchanged.
5. Test DPI/text scaling and that sensitive values never appear in previews, dialogs or logs.
6. Review the final source tree for actual vaults, credentials, personal settings, signing secrets, artifacts and network permissions before any public push.

## Publication separation

The `main` branch of `geniasoftwin/Genia-Password` intentionally contains only the **regular editions** (Windows 2.1.1 and Android Mobile 1.0). To publish PRO source code, first create/connect a **separate dedicated PRO repository**, then import the reviewed Windows PRO and Android PRO projects there. Do not replace the regular `Windows/` or `Android/` trees with PRO sources.

Previous status: [PRO WorkTest — 29 October 2026](PRO_WORKTEST_2026-10-29.md).
