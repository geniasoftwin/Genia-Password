# GeniaPassword Vault v2 — interop reference vector

This vector was generated independently from the Android implementation with standard Argon2id v1.3 and AES-256-GCM. It exists to catch byte-level differences in password encoding, AAD composition, key wrapping, or field names.

## KDF

- Password (UTF-8): `interop-Päss-密码`
- Salt hex: `000102030405060708090a0b0c0d0e0f`
- KDF: `Argon2id`, version 1.3
- iterations: `3`
- memoryKiB: `65536`
- parallelism: `2`
- output: 32 bytes
- expected UnlockKey hex: `4bfacf618896bb3bd5960ab187d8898cc025d9d443df3863edd7e8d2a945cfb1`
- deterministic test VaultKey hex: `202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f`

## AAD contract

Header AAD is UTF-8 with no newline or NUL terminator:

`GeniaPassword|2|Argon2id|3|65536|2|AAECAwQFBgcICQoLDA0ODw==|7|1787600000123`

Key wrapping AAD is header + `|key-wrap`.
Data AAD is header + `|data`.

## Deterministic envelope

The deterministic nonces/keys below are **test data only**, never production defaults.

```json
{
  "version": 2,
  "kdf": "Argon2id",
  "iterations": 3,
  "memoryKiB": 65536,
  "parallelism": 2,
  "salt": "AAECAwQFBgcICQoLDA0ODw==",
  "wrappedKeyNonce": "AAECAwQFBgcICQoL",
  "wrappedKey": "I6suJQrrKKdomhXa7KCSayWDQR3LGv3lAg4SRinywJI=",
  "wrappedKeyTag": "gNCS+JYXGK0+Gz0cReP9Kg==",
  "nonce": "DA0ODxAREhMUFRYX",
  "ciphertext": "mRgrW0BPadqtd96vifeKo4ysCYb6nah1lumOMRk8unXCPTNwTD0iNtV5BTumVgCDWXyxgtRkME8uzBxM+Z7Fm/QWzFeR4PPdw5ZgCEDgAt1Ui3MZ+23KTPN4QgtZpRAhyQnXQ0HxIJS8Vy14qbHk1qC3GKXOqHup+hf1wVQCLXRYGln1JhB1IQyZ6EeByiAxhMfElOf5PgOOHncjQvGP7GKyyir0BThMn5AKJt5+ksebFZGtvDVvLGbqsQco3tOXw3SphjSX7vyDIOZLV++9nMgiiyKgIOkUyco1dkRWVJVWi6Y6isUV7zzKvAWP5/v/XVWAL6mkc3D+L7ObmwExjTFm5HZOLMQF8Q9U+QuJcAyL4Jck13tdXbMfI1VdXI91TNhI4ZamzCqCGhdERlCFGkLD7gZWZjMvSjDm19lSjc45VJn05pvobaBNLVa0PGl7/Q/FwptvHeUYodCLB2GcSSpkwnynBKbUtJxi1Fi9NjJFZBYmOaJT2hK6f270RdD5gRDbLpQA+zpN3yghMFudU3teN9d9Bf5xP2cxtsjbcghfuYz3h5vKDWICVSMMIhJbGfuD0ZVXu93G3ceH0A1eNtdu8L4njZnSkPvAbdd7r6FSNGUZRCuQ/DfoQRR8i9ol14jvV0jFYJ6wE57z5pp9IvQF9jx7ubo4rb27d98PWgULt8BtWiI=",
  "tag": "J3lgChJoRf1296o3n5ejmg==",
  "revision": 7,
  "savedUtcUnixMilliseconds": 1787600000123
}
```

Expected decrypted entry title: `Desktop fixture`.
The plaintext also contains unknown root/entry/custom-field properties plus `passwordHistory`; Android Beta4 must preserve those JSON properties when saving rather than silently dropping them.

## Expected plaintext fixture

```json
{"entries":[{"id":"11111111-2222-3333-4444-555555555555","title":"Desktop fixture","website":"https://example.test","userName":"user","password":"secret","notes":"line1\nline2","customFields":[{"name":"OTP","value":"123","isSecret":true,"desktopOnly":"keep-me"}],"updatedUtc":"2026-08-25T10:00:00+00:00","passwordUpdatedUtc":"2026-08-24T10:00:00+00:00","passwordHistory":[{"password":"old-secret","changedUtc":"2026-08-24T10:00:00+00:00","futureField":42}],"futureEntryField":{"x":1}}],"futureRootField":"preserve-me"}
```

The `changedUtc`/`futureField` names in this synthetic PasswordHistory object are fixture-only opaque fields; they do **not** claim to define the Windows PasswordHistory item schema. The preservation requirement is simply that an existing history array survives Android round-trip unchanged unless Android explicitly edits that model in a future version.
