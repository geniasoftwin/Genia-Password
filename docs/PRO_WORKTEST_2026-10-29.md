# GeniaPassword PRO WorkTest — 29 October 2026

> **Status: user-tested WorkTest, not a published production release.**

The **PRO** editions are separate from the regular GeniaPassword editions in this repository. Their source archives are maintained separately and have **not** been uploaded to this public repository. This page tracks the latest reported PRO test state without replacing the regular source or inadvertently mixing editions.

## Windows PRO 1.2 — OfflineMerge

Latest tested source package: `GeniaPassword_PRO_1_2_OfflineMerge_ExportCurrent_NoOpMerge_WorkTest_2026-10-29.zip`.

- Management window has six equal-size action buttons arranged in a 2-column × 3-row grid, with compact storage information.
- Merge workflow imports a user-selected encrypted `.pnb` and previews additions, skipped entries and conflicts.
- If merge finds zero new entries, the preview shows only a close action and does not commit a change or create a backup.
- The **Create current .pnb copy** action exports the latest *saved* encrypted vault to a chosen destination without overwriting the working vault. The export code checks source stability and verifies the destination SHA-256 hash against the saved source.
- Existing backup tools, duplicate removal and master-password management remain available.

Build the Windows PRO source on Windows with the .NET 10 SDK:

```powershell
.\BUILD_PORTABLE.cmd
```

## Android Mobile PRO 1.1 — OfflineMerge

Latest tested source package: `GeniaPasswordMobile_PRO_1_1_OfflineMerge_NoOpPreview_WorkTest_2026-10-29.zip`.

- Management actions appear as four full-width vertically stacked buttons.
- Offline `.pnb` merge includes preview and no-op close behavior when zero new records are detected.
- Local encrypted backup creation and file export are available.
- Master-password change requires the old master password and uses the existing Vault v2 backup / atomic replacement path; prior biometric unlock material is invalidated after a successful rotation.
- The Android application does not request the `INTERNET` permission.

Build Mobile PRO source with the Android SDK and compatible JDK:

```powershell
.\gradlew.bat clean test assembleDebug
```

APK output: `app\build\outputs\apk\debug\app-debug.apk`.

## Verification and publication boundaries

The project owner reported successful application-level testing of these PRO changes on 29 October 2026. That report is valuable but **not** a full formal release validation. Dedicated release verification should still cover backup restore, disposable-vault transfer between platforms, master-password rotation, biometric unlock, merge conflicts and DPI/Android font scaling.

Both PRO work packages were created with the goal of preserving Vault v2, Argon2id, AES-256-GCM, offline-only operation and separate local vaults. This page is a status record, not an independent security certification.

**Do not commit:** real `.pnb` vaults, backup files, credentials, release keystores, `local.properties`, APK/EXE build output, or user-specific configuration.

A dedicated PRO repository is required before publishing these separate PRO source projects. The regular `Windows/` and `Android/` trees on `main` intentionally remain unchanged.
