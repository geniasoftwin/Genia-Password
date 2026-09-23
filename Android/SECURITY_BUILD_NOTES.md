# Beta6.4 secure build notes

1. The source archive intentionally contains no private signing key and no dependency-verification metadata bootstrapped on an unknown machine.
2. The bundled wrapper JAR SHA-256 is `76805e32c009c0cf0dd5d206bddc9fb22ea42e84db904b764f3047de095493f3`, an official Gradle 9.0/9.1 wrapper JAR. `REFRESH_GRADLE_WRAPPER.ps1` checks this value BEFORE executing it and then requires the official Gradle 9.7.0 wrapper JAR SHA-256 `7a9ce74cff467ca1bf60a4fcd9f05185acceda4d0f382434d393e17864262c5d`.
3. `gradle-wrapper.properties` pins Gradle 9.7.0 `-bin` distribution SHA-256 to `84fbba45c7f4c64abc77460e1c00f541e9f960e3c7ed2538f1ede19eacd873ae`.
4. Generate `gradle/verification-metadata.xml` only on a trusted build machine/network, review it, then archive it with the release source.
5. The permanent Android signing keystore must live outside the project directory and have at least two offline backup copies.
6. Do not promote `app-debug.apk` to production. Run `VERIFY_RELEASE_APK.ps1` against the signed release APK before distribution.
