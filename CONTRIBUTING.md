# Contributing to GeniaPassword

Thanks for helping improve GeniaPassword.

## Project principles

Changes should preserve the project's core properties:

- offline password management;
- no cloud account requirement;
- no telemetry, advertising SDKs, or hidden network behavior;
- compatibility and integrity of the encrypted vault are treated as security-sensitive;
- simple, predictable UI behavior.

## Before opening a pull request

- build and test the affected platform;
- do not commit vault files, backups, signing keys, passwords, API tokens, `local.properties`, or build output;
- explain any change to vault serialization, KDF, encryption, backup, clipboard handling, biometric handling, or authentication logic;
- keep third-party dependency additions minimal and document their license and purpose.

## Security issues

Do not disclose exploitable vulnerabilities or real secrets in a public issue. Follow [SECURITY.md](SECURITY.md).

## License

By contributing code to this repository, you agree that your contribution is provided under the repository's GPL-3.0-or-later license.
