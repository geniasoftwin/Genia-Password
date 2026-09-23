# GeniaPassword Mobile 1.0 Final

Финальная 1.0 построена на проверенной базе Beta6.4.1 / Vault v2. Формат Windows ↔ Android не менялся: Argon2id + wrapped VaultKey + AES-256-GCM остаются совместимыми с desktop Vault v2.


## Финальная полировка 1.0

- Главный экран стал компактнее: уменьшены логотип, заголовок и управляющие элементы.
- На главном экране оставлены количество записей, поиск и создание записи; импорт/экспорт находятся в «Управлении».
- Техническая информация о Vault показывается в «Управлении», а не в основном каталоге.
- Поиск поддерживает несколько слов и ищет по названию, логину, сайту, заметкам и несекретным дополнительным полям.
- Криптографическое ядро, репозиторий, backup-механизм, биометрия и Android manifest не менялись.
- Приложение остаётся полностью офлайн: permission `INTERNET` отсутствует.

## Что усилено

- Убраны полные `preservedRootJson` / `preservedJson` копии расшифрованного vault. Android хранит только typed-поля + bounded unknown desktop properties + bounded opaque `passwordHistory`.
- Слабый импортированный Vault v2 (`memory < 64 MiB` или `iterations < 3`) после успешного мастер-пароля автоматически перешифровывается текущим профилем Argon2id 64 MiB / t=3.
- Для untrusted Vault v2 введён адаптивный лимит Argon2 по памяти устройства и абсолютный hard cap 256 MiB.
- Импорт защищён от rollback/branch conflict через `revision + SHA-256 + last-synced fingerprint`.
- После экспорта файл повторно открывается через SAF и сверяется по SHA-256.
- Временный и финальный `vault.pnb` сверяются по SHA-256 при сохранении; обязательный backup до замены сохранён.
- На Android 8/9 копирование **пароля** в системный clipboard отключено. На всех версиях clip помечается sensitive; очистка остаётся через 30 секунд и при Lock.
- `android:allowBackup=false`, полный exclude cloud/device-transfer, `FLAG_SECURE`, overlay protection, Autofill/Content Capture disabled сохранены.
- `android:usesCleartextTraffic=false`; permission `INTERNET` отсутствует.
- Gradle 9.7.0 distribution pinned по SHA-256.
- Debug/Release разделены явно; Release `debuggable=false`, R8/minify/resource shrink включены.
- Release невозможно собрать без постоянного signing key, официального Gradle wrapper JAR и `gradle/verification-metadata.xml`.
- Добавлены unit/instrumentation security regression tests: KDF-policy, sync conflict, AAD/revision/tag/ciphertext tamper, unknown-field round-trip.

## Сборка

```powershell
$Sdk = "$env:LOCALAPPDATA\Android\Sdk"
"sdk.dir=$($Sdk -replace '\\','/')" | Set-Content -Encoding ASCII .\local.properties
.\gradlew.bat clean test assembleDebug
```

APK:

`app\build\outputs\apk\debug\app-debug.apk`

**Debug APK — только для тестирования. Не храните в нём единственную копию реального vault.**

## Подготовка production Release

Перед первым Release:

```powershell
.\REFRESH_GRADLE_WRAPPER.ps1
.\PIN_DEPENDENCIES.ps1
.\CREATE_RELEASE_KEY.ps1 -Output "E:\\GENIAPASSWORD_PRIVATE\\geniapassword-release.jks"
```

Ключ подписи создавайте ВНЕ папки проекта и храните минимум в двух офлайн-копиях. Никогда не добавляйте `.jks/.keystore` в архив проекта или облачную синхронизацию.

Сборка Release:

```powershell
.\BUILD_SECURE_RELEASE.ps1 -Keystore "D:\SECURE\geniapassword-release.jks"
.\VERIFY_RELEASE_APK.ps1
```

Release APK:

`app\build\outputs\apk\release\app-release.apk`

## Важное ограничение перехода Debug → Release

Android не установит APK с новым production signing key поверх старой debug-подписи. Перед первой миграцией на Release обязательно экспортируйте и отдельно проверьте `vault.pnb`, затем установите Release и импортируйте проверенную копию.

## Offline-принцип

GeniaPassword Mobile не имеет `INTERNET` permission, аккаунта, облака, телеметрии или сервера. Импорт/экспорт выполняется через Android Storage Access Framework и локальные файлы.


## Beta6.4.1 build fix
Исправлена совместимость `securitySanityCheck` с Gradle 9.7 Configuration Cache. Криптографический формат Vault v2 не менялся.
