# Security Policy

## Supported editions

Security reports are accepted for the current regular Windows and Android editions published in this repository.

## Reporting a vulnerability

Please do **not** publish exploitable security details, real vault files, master passwords, signing keys, or other secrets in a public issue.

When reporting a security problem, include:

- affected platform and version;
- steps to reproduce;
- expected and actual behavior;
- whether the issue can cause vault corruption, plaintext disclosure, authentication bypass, or loss of integrity;
- a minimal reproduction that contains no real secrets.

If private reporting is available for this repository, prefer it for vulnerabilities that could expose user data.

## Security design notes

GeniaPassword is an offline password manager. The regular Android application is designed without the `INTERNET` permission. The project does not require an online account or cloud service.

Vault encryption uses the project's Vault v2 format with Argon2id key derivation and AES-256-GCM authenticated encryption.

## Out of scope

The application cannot protect secrets after the operating system or user session is fully compromised. Examples include malware with sufficient privileges, hostile screen capture, hardware compromise, or a user intentionally exporting plaintext data.

## Sensitive files

Never attach or commit:

- `vault.pnb` or vault backups;
- Android signing keystores;
- signing passwords or environment secrets;
- `local.properties`;
- private certificates or private keys;
- real exported password data.

## Master-password recovery

There is no remote recovery service or backdoor. Losing the master password can make the vault unrecoverable.
