package com.geniapassword.mobile.data

import org.bouncycastle.crypto.generators.Argon2BytesGenerator
import org.bouncycastle.crypto.params.Argon2Parameters
import org.json.JSONObject
import java.nio.CharBuffer
import java.nio.charset.StandardCharsets
import java.security.GeneralSecurityException
import java.security.SecureRandom
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

internal data class VaultKdfParameters(
    val name: String,
    val iterations: Int,
    val memoryKiB: Int,
    val parallelism: Int,
)

internal data class VaultEnvelopeInfo(
    val version: Int,
    val revision: Long?,
    val kdf: VaultKdfParameters?,
)

internal object VaultCrypto {
    const val LEGACY_FORMAT_VERSION = 1
    const val CURRENT_FORMAT_VERSION = 2
    const val LEGACY_DEFAULT_ITERATIONS = 600_000
    const val SALT_SIZE = 16
    const val KEY_SIZE = 32
    const val NONCE_SIZE = 12
    const val TAG_SIZE = 16

    private const val LEGACY_KDF_NAME = "PBKDF2-SHA256"
    private const val ARGON2ID_NAME = "Argon2id"
    private const val LEGACY_MOBILE_AAD_PREFIX = "PasswordNotebook"
    private const val DESKTOP_AAD_PREFIX = "GeniaPassword"

    // Android-created v2 vaults use a conservative mobile profile. The parameters are
    // serialized into the envelope, so Windows does not need to use identical defaults.
    const val DEFAULT_ARGON2_ITERATIONS = 3
    const val DEFAULT_ARGON2_MEMORY_KIB = 65_536
    const val DEFAULT_ARGON2_PARALLELISM = 2

    // Hard caps for untrusted files: enough for the desktop profile, but bounded so a
    // malicious file cannot demand gigabytes of RAM or minutes of CPU on the phone.
    private const val MIN_ARGON2_ITERATIONS = 1
    private const val MAX_ARGON2_ITERATIONS = 10
    private const val MIN_ARGON2_MEMORY_KIB = 8 * 1024
    private const val MAX_ARGON2_MEMORY_KIB = VaultSecurityPolicy.HARD_MAX_ARGON2_MEMORY_KIB
    private const val MIN_ARGON2_PARALLELISM = 1
    private const val MAX_ARGON2_PARALLELISM = 8

    private const val MIN_LEGACY_ITERATIONS = 100_000
    private const val MAX_LEGACY_ITERATIONS = 2_000_000

    val currentKdf = VaultKdfParameters(
        ARGON2ID_NAME,
        DEFAULT_ARGON2_ITERATIONS,
        DEFAULT_ARGON2_MEMORY_KIB,
        DEFAULT_ARGON2_PARALLELISM,
    )

    private val random = SecureRandom()

    data class OpenedVault(
        val data: VaultData,
        val vaultKey: ByteArray,
        val unlockKey: ByteArray,
        val salt: ByteArray,
        val kdf: VaultKdfParameters,
        val revision: Long,
        val needsMigration: Boolean,
    )

    private data class LegacyEnvelope(
        val iterations: Int,
        val salt: ByteArray,
        val nonce: ByteArray,
        val ciphertext: ByteArray,
        val tag: ByteArray,
    )

    private data class V2Envelope(
        val kdf: VaultKdfParameters,
        val saltBase64: String,
        val salt: ByteArray,
        val wrappedKeyNonce: ByteArray,
        val wrappedKey: ByteArray,
        val wrappedKeyTag: ByteArray,
        val nonce: ByteArray,
        val ciphertext: ByteArray,
        val tag: ByteArray,
        val revision: Long,
        val savedUtcUnixMilliseconds: Long,
    )

    fun createSalt(): ByteArray = ByteArray(SALT_SIZE).also(random::nextBytes)
    fun createVaultKey(): ByteArray = ByteArray(KEY_SIZE).also(random::nextBytes)

    fun deriveCurrentUnlockKey(password: CharArray, salt: ByteArray): ByteArray =
        deriveArgon2id(password, salt, currentKdf)

    fun deriveArgon2id(password: CharArray, salt: ByteArray, kdf: VaultKdfParameters): ByteArray {
        if (password.isEmpty()) throw VaultException("Мастер-пароль не задан.")
        if (salt.size != SALT_SIZE) throw VaultException("Некорректная соль шифрования.")
        validateArgon2(kdf)

        val passwordBytes = encodeUtf8(password)
        val output = ByteArray(KEY_SIZE)
        val parameters = Argon2Parameters.Builder(Argon2Parameters.ARGON2_id)
            .withVersion(Argon2Parameters.ARGON2_VERSION_13)
            .withSalt(salt)
            .withIterations(kdf.iterations)
            .withMemoryAsKB(kdf.memoryKiB)
            .withParallelism(kdf.parallelism)
            .build()
        return try {
            val generator = Argon2BytesGenerator()
            generator.init(parameters)
            generator.generateBytes(passwordBytes, output)
            output
        } catch (error: Exception) {
            output.fill(0)
            throw VaultException("Не удалось сформировать Argon2id-ключ.", error)
        } finally {
            passwordBytes.fill(0)
            parameters.clear()
        }
    }

    fun decrypt(
        envelopeBytes: ByteArray,
        password: CharArray,
        maxArgon2MemoryKiB: Int = VaultSecurityPolicy.HARD_MAX_ARGON2_MEMORY_KIB,
    ): OpenedVault {
        if (password.isEmpty()) throw VaultException("Мастер-пароль не задан.")
        val root = parseRoot(envelopeBytes)
        val version = readVaultVersion(root)
        return when (version) {
            CURRENT_FORMAT_VERSION -> decryptV2(parseV2Envelope(root, maxArgon2MemoryKiB), password)
            LEGACY_FORMAT_VERSION -> decryptLegacyAndPrepareMigration(parseLegacyEnvelope(root), password)
            else -> throw VaultException(
                "Файл распознан как Vault v$version. Beta6.4.1 поддерживает только Vault v1 и v2."
            )
        }
    }

    fun decryptWithUnlockKey(envelopeBytes: ByteArray, unlockKey: ByteArray): OpenedVault {
        if (unlockKey.size != KEY_SIZE) throw VaultException("Некорректный ключ разблокировки.")
        val root = parseRoot(envelopeBytes)
        if (readVaultVersion(root) != CURRENT_FORMAT_VERSION) {
            throw VaultException("Для старого хранилища требуется мастер-пароль один раз для миграции на Vault v2.")
        }
        return decryptV2WithUnlockKey(
            parseV2Envelope(root, VaultSecurityPolicy.HARD_MAX_ARGON2_MEMORY_KIB),
            unlockKey.copyOf(),
        )
    }

    fun inspectEnvelope(envelopeBytes: ByteArray): VaultEnvelopeInfo {
        val root = parseRoot(envelopeBytes)
        return when (val version = readVaultVersion(root)) {
            CURRENT_FORMAT_VERSION -> {
                val kdf = VaultKdfParameters(
                    name = readRequiredString(root, "kdf"),
                    iterations = readRequiredInt(root, "iterations"),
                    memoryKiB = readRequiredInt(root, "memoryKiB"),
                    parallelism = readRequiredInt(root, "parallelism"),
                )
                validateArgon2(kdf)
                val revision = readRequiredLong(root, "revision")
                if (revision <= 0L) throw VaultException("Метаданные revision vault v2 повреждены.")
                VaultEnvelopeInfo(version = version, revision = revision, kdf = kdf)
            }
            LEGACY_FORMAT_VERSION -> VaultEnvelopeInfo(version = version, revision = null, kdf = null)
            else -> VaultEnvelopeInfo(version = version, revision = null, kdf = null)
        }
    }

    fun needsKdfUpgrade(kdf: VaultKdfParameters): Boolean = VaultSecurityPolicy.needsKdfUpgrade(kdf)

    fun encryptV2(
        data: VaultData,
        vaultKey: ByteArray,
        unlockKey: ByteArray,
        salt: ByteArray,
        kdf: VaultKdfParameters,
        revision: Long,
        savedUtcUnixMilliseconds: Long,
    ): ByteArray {
        if (vaultKey.size != KEY_SIZE || unlockKey.size != KEY_SIZE) {
            throw VaultException("Некорректный ключ шифрования.")
        }
        if (salt.size != SALT_SIZE) throw VaultException("Некорректная соль шифрования.")
        validateArgon2(kdf)
        if (revision <= 0 || savedUtcUnixMilliseconds <= 0) {
            throw VaultException("Некорректная ревизия или дата сохранения vault.")
        }

        val plaintext = VaultJson.encode(data)
        if (plaintext.size > VaultConstraints.MAX_CIPHERTEXT_BYTES) {
            plaintext.fill(0)
            throw VaultException("Хранилище превышает допустимый размер.")
        }

        val saltBase64 = Base64.getEncoder().encodeToString(salt)
        val headerAad = buildV2HeaderAad(kdf, saltBase64, revision, savedUtcUnixMilliseconds)
        val keyAad = addPurpose(headerAad, "key-wrap")
        val dataAad = addPurpose(headerAad, "data")
        val wrappedKeyNonce = ByteArray(NONCE_SIZE).also(random::nextBytes)
        val dataNonce = ByteArray(NONCE_SIZE).also(random::nextBytes)

        var wrappedCombined: ByteArray? = null
        var dataCombined: ByteArray? = null
        try {
            wrappedCombined = aesGcmEncrypt(unlockKey, wrappedKeyNonce, vaultKey, keyAad)
            dataCombined = aesGcmEncrypt(vaultKey, dataNonce, plaintext, dataAad)

            val wrappedKey = wrappedCombined.copyOfRange(0, wrappedCombined.size - TAG_SIZE)
            val wrappedKeyTag = wrappedCombined.copyOfRange(wrappedCombined.size - TAG_SIZE, wrappedCombined.size)
            val ciphertext = dataCombined.copyOfRange(0, dataCombined.size - TAG_SIZE)
            val dataTag = dataCombined.copyOfRange(dataCombined.size - TAG_SIZE, dataCombined.size)
            try {
                return JSONObject()
                    .put("version", CURRENT_FORMAT_VERSION)
                    .put("kdf", kdf.name)
                    .put("iterations", kdf.iterations)
                    .put("memoryKiB", kdf.memoryKiB)
                    .put("parallelism", kdf.parallelism)
                    .put("salt", saltBase64)
                    .put("wrappedKeyNonce", Base64.getEncoder().encodeToString(wrappedKeyNonce))
                    .put("wrappedKey", Base64.getEncoder().encodeToString(wrappedKey))
                    .put("wrappedKeyTag", Base64.getEncoder().encodeToString(wrappedKeyTag))
                    .put("nonce", Base64.getEncoder().encodeToString(dataNonce))
                    .put("ciphertext", Base64.getEncoder().encodeToString(ciphertext))
                    .put("tag", Base64.getEncoder().encodeToString(dataTag))
                    .put("revision", revision)
                    .put("savedUtcUnixMilliseconds", savedUtcUnixMilliseconds)
                    .toString(2)
                    .toByteArray(StandardCharsets.UTF_8)
            } finally {
                wrappedKey.fill(0)
                wrappedKeyTag.fill(0)
                ciphertext.fill(0)
                dataTag.fill(0)
            }
        } finally {
            plaintext.fill(0)
            headerAad.fill(0)
            keyAad.fill(0)
            dataAad.fill(0)
            wrappedKeyNonce.fill(0)
            dataNonce.fill(0)
            wrappedCombined?.fill(0)
            dataCombined?.fill(0)
        }
    }

    private fun decryptV2(envelope: V2Envelope, password: CharArray): OpenedVault {
        val unlockKey = deriveArgon2id(password, envelope.salt, envelope.kdf)
        return decryptV2WithUnlockKey(envelope, unlockKey)
    }

    private fun decryptV2WithUnlockKey(envelope: V2Envelope, unlockKey: ByteArray): OpenedVault {
        if (unlockKey.size != KEY_SIZE) throw VaultException("Некорректный ключ разблокировки.")
        val headerAad = buildV2HeaderAad(
            envelope.kdf,
            envelope.saltBase64,
            envelope.revision,
            envelope.savedUtcUnixMilliseconds,
        )
        val keyAad = addPurpose(headerAad, "key-wrap")
        val dataAad = addPurpose(headerAad, "data")
        var vaultKey: ByteArray? = null
        var plaintext: ByteArray? = null
        try {
            vaultKey = aesGcmDecrypt(
                unlockKey,
                envelope.wrappedKeyNonce,
                envelope.wrappedKey,
                envelope.wrappedKeyTag,
                keyAad,
            )
            if (vaultKey.size != KEY_SIZE) throw VaultException("Ключ хранилища повреждён.")
            plaintext = aesGcmDecrypt(
                vaultKey,
                envelope.nonce,
                envelope.ciphertext,
                envelope.tag,
                dataAad,
            )
            val data = VaultJson.decode(plaintext)
            return OpenedVault(
                data = data,
                vaultKey = vaultKey,
                unlockKey = unlockKey,
                salt = envelope.salt,
                kdf = envelope.kdf,
                revision = envelope.revision,
                needsMigration = false,
            ).also {
                // ownership was transferred into OpenedVault
                vaultKey = null
            }
        } catch (error: VaultException) {
            unlockKey.fill(0)
            throw error
        } catch (error: GeneralSecurityException) {
            unlockKey.fill(0)
            throw VaultException("Не удалось открыть хранилище. Проверьте мастер-пароль и целостность файла.", error)
        } catch (error: Exception) {
            unlockKey.fill(0)
            throw VaultException("Не удалось открыть хранилище. Проверьте мастер-пароль и целостность файла.", error)
        } finally {
            plaintext?.fill(0)
            vaultKey?.fill(0)
            headerAad.fill(0)
            keyAad.fill(0)
            dataAad.fill(0)
            envelope.clearExceptSaltForTransfer()
        }
    }

    private fun decryptLegacyAndPrepareMigration(envelope: LegacyEnvelope, password: CharArray): OpenedVault {
        val legacyKey = deriveLegacyPbkdf2(password, envelope.salt, envelope.iterations)
        var plaintext: ByteArray? = null
        try {
            // Early Android betas used PasswordNotebook as AAD. Desktop legacy v1 uses
            // GeniaPassword. Try mobile first, then desktop, without changing the file.
            val decrypted = try {
                aesGcmDecrypt(
                    legacyKey,
                    envelope.nonce,
                    envelope.ciphertext,
                    envelope.tag,
                    legacyMobileAad(envelope.iterations),
                )
            } catch (_: GeneralSecurityException) {
                aesGcmDecrypt(
                    legacyKey,
                    envelope.nonce,
                    envelope.ciphertext,
                    envelope.tag,
                    desktopLegacyAad(envelope.iterations),
                )
            }
            plaintext = decrypted
            val data = VaultJson.decode(decrypted)
            val newSalt = createSalt()
            val newUnlockKey = deriveCurrentUnlockKey(password, newSalt)
            val newVaultKey = createVaultKey()
            return OpenedVault(
                data = data,
                vaultKey = newVaultKey,
                unlockKey = newUnlockKey,
                salt = newSalt,
                kdf = currentKdf,
                revision = 0,
                needsMigration = true,
            )
        } catch (error: VaultException) {
            throw error
        } catch (error: GeneralSecurityException) {
            throw VaultException("Не удалось открыть старое хранилище. Проверьте мастер-пароль и целостность файла.", error)
        } finally {
            legacyKey.fill(0)
            plaintext?.fill(0)
            envelope.clear()
        }
    }

    private fun parseV2Envelope(root: JSONObject, maxArgon2MemoryKiB: Int): V2Envelope {
        try {
            val kdf = VaultKdfParameters(
                name = readRequiredString(root, "kdf"),
                iterations = readRequiredInt(root, "iterations"),
                memoryKiB = readRequiredInt(root, "memoryKiB"),
                parallelism = readRequiredInt(root, "parallelism"),
            )
            validateArgon2(kdf, maxArgon2MemoryKiB)
            val revision = readRequiredLong(root, "revision")
            val savedUtc = readRequiredLong(root, "savedUtcUnixMilliseconds")
            if (revision <= 0 || savedUtc <= 0) throw VaultException("Метаданные ревизии vault v2 повреждены.")
            val saltBase64 = readRequiredString(root, "salt")
            return V2Envelope(
                kdf = kdf,
                saltBase64 = saltBase64,
                salt = decodeBase64(saltBase64, "salt", SALT_SIZE),
                wrappedKeyNonce = decodeField(root, "wrappedKeyNonce", NONCE_SIZE),
                wrappedKey = decodeField(root, "wrappedKey", KEY_SIZE),
                wrappedKeyTag = decodeField(root, "wrappedKeyTag", TAG_SIZE),
                nonce = decodeField(root, "nonce", NONCE_SIZE),
                ciphertext = decodeField(root, "ciphertext", null, VaultConstraints.MAX_CIPHERTEXT_BYTES),
                tag = decodeField(root, "tag", TAG_SIZE),
                revision = revision,
                savedUtcUnixMilliseconds = savedUtc,
            )
        } catch (error: VaultException) {
            throw error
        } catch (error: Exception) {
            throw VaultException("Vault v2 повреждён или имеет неверный формат.", error)
        }
    }

    private fun parseLegacyEnvelope(root: JSONObject): LegacyEnvelope {
        val kdf = readRequiredString(root, "kdf")
        if (kdf != LEGACY_KDF_NAME) throw VaultException("Алгоритм старого хранилища не поддерживается.")
        val iterations = readRequiredInt(root, "iterations")
        validateLegacyIterations(iterations)
        return LegacyEnvelope(
            iterations = iterations,
            salt = decodeField(root, "salt", SALT_SIZE),
            nonce = decodeField(root, "nonce", NONCE_SIZE),
            ciphertext = decodeField(root, "ciphertext", null, VaultConstraints.MAX_CIPHERTEXT_BYTES),
            tag = decodeField(root, "tag", TAG_SIZE),
        )
    }

    private fun parseRoot(envelopeBytes: ByteArray): JSONObject {
        if (envelopeBytes.isEmpty() || envelopeBytes.size > VaultConstraints.MAX_VAULT_BYTES) {
            throw VaultException("Файл хранилища пуст или слишком большой.")
        }
        return try {
            JSONObject(String(envelopeBytes, StandardCharsets.UTF_8))
        } catch (error: Exception) {
            throw VaultException("Файл хранилища имеет неверный формат.", error)
        }
    }

    private fun deriveLegacyPbkdf2(password: CharArray, salt: ByteArray, iterations: Int): ByteArray {
        if (password.isEmpty()) throw VaultException("Мастер-пароль не задан.")
        validateLegacyIterations(iterations)
        if (salt.size != SALT_SIZE) throw VaultException("Некорректная соль шифрования.")

        val passwordBytes = encodeUtf8(password)
        val block = ByteArray(salt.size + 4)
        salt.copyInto(block)
        block[block.lastIndex] = 1
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(passwordBytes, "HmacSHA256"))
        var u = mac.doFinal(block)
        val result = u.clone()
        try {
            for (iteration in 1 until iterations) {
                val next = mac.doFinal(u)
                u.fill(0)
                u = next
                for (index in result.indices) {
                    result[index] = (result[index].toInt() xor u[index].toInt()).toByte()
                }
            }
            return result.copyOf(KEY_SIZE)
        } finally {
            passwordBytes.fill(0)
            block.fill(0)
            u.fill(0)
            result.fill(0)
        }
    }

    private fun aesGcmEncrypt(key: ByteArray, nonce: ByteArray, plaintext: ByteArray, aad: ByteArray): ByteArray {
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(TAG_SIZE * 8, nonce))
        cipher.updateAAD(aad)
        return cipher.doFinal(plaintext)
    }

    @Throws(GeneralSecurityException::class)
    private fun aesGcmDecrypt(
        key: ByteArray,
        nonce: ByteArray,
        ciphertext: ByteArray,
        tag: ByteArray,
        aad: ByteArray,
    ): ByteArray {
        val combined = ByteArray(ciphertext.size + tag.size)
        ciphertext.copyInto(combined, 0)
        tag.copyInto(combined, ciphertext.size)
        return try {
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(TAG_SIZE * 8, nonce))
            cipher.updateAAD(aad)
            cipher.doFinal(combined)
        } finally {
            combined.fill(0)
        }
    }

    private fun buildV2HeaderAad(
        kdf: VaultKdfParameters,
        saltBase64: String,
        revision: Long,
        savedUtcUnixMilliseconds: Long,
    ): ByteArray =
        "$DESKTOP_AAD_PREFIX|$CURRENT_FORMAT_VERSION|${kdf.name}|${kdf.iterations}|${kdf.memoryKiB}|${kdf.parallelism}|$saltBase64|$revision|$savedUtcUnixMilliseconds"
            .toByteArray(StandardCharsets.UTF_8)

    private fun addPurpose(headerAad: ByteArray, purpose: String): ByteArray {
        val purposeBytes = "|$purpose".toByteArray(StandardCharsets.UTF_8)
        return ByteArray(headerAad.size + purposeBytes.size).also { result ->
            headerAad.copyInto(result, 0)
            purposeBytes.copyInto(result, headerAad.size)
            purposeBytes.fill(0)
        }
    }

    private fun legacyMobileAad(iterations: Int): ByteArray =
        "$LEGACY_MOBILE_AAD_PREFIX|$LEGACY_FORMAT_VERSION|$LEGACY_KDF_NAME|$iterations"
            .toByteArray(StandardCharsets.UTF_8)

    private fun desktopLegacyAad(iterations: Int): ByteArray =
        "$DESKTOP_AAD_PREFIX|$LEGACY_FORMAT_VERSION|$LEGACY_KDF_NAME|$iterations"
            .toByteArray(StandardCharsets.UTF_8)

    private fun encodeUtf8(password: CharArray): ByteArray {
        val encoded = StandardCharsets.UTF_8.newEncoder().encode(CharBuffer.wrap(password))
        val bytes = ByteArray(encoded.remaining())
        encoded.get(bytes)
        if (encoded.hasArray()) encoded.array().fill(0)
        return bytes
    }

    private fun validateArgon2(
        kdf: VaultKdfParameters,
        maxMemoryKiB: Int = MAX_ARGON2_MEMORY_KIB,
    ) {
        if (!kdf.name.equals(ARGON2ID_NAME, ignoreCase = true)) {
            throw VaultException("Vault v2 распознан, но KDF '${kdf.name}' не поддерживается Beta6.4.1.")
        }
        if (kdf.iterations !in MIN_ARGON2_ITERATIONS..MAX_ARGON2_ITERATIONS ||
            kdf.memoryKiB !in MIN_ARGON2_MEMORY_KIB..MAX_ARGON2_MEMORY_KIB ||
            kdf.parallelism !in MIN_ARGON2_PARALLELISM..MAX_ARGON2_PARALLELISM
        ) {
            throw VaultException("Параметры Argon2id vault v2 выходят за абсолютные безопасные пределы Android.")
        }
        val effectiveDeviceLimit = maxMemoryKiB.coerceIn(MIN_ARGON2_MEMORY_KIB, MAX_ARGON2_MEMORY_KIB)
        if (kdf.memoryKiB > effectiveDeviceLimit) {
            throw VaultException(
                "Vault требует ${kdf.memoryKiB / 1024} MiB памяти Argon2id, а безопасный лимит этого устройства — " +
                    "${effectiveDeviceLimit / 1024} MiB. Импорт остановлен для защиты от зависания/OOM."
            )
        }
    }

    private fun validateLegacyIterations(iterations: Int) {
        if (iterations !in MIN_LEGACY_ITERATIONS..MAX_LEGACY_ITERATIONS) {
            throw VaultException("Некорректные параметры формирования ключа старого хранилища.")
        }
    }

    private fun decodeField(root: JSONObject, name: String, expectedSize: Int?, maxSize: Int? = expectedSize): ByteArray =
        decodeBase64(readRequiredString(root, name), name, expectedSize, maxSize)

    private fun decodeBase64(value: String, name: String, expectedSize: Int?, maxSize: Int? = expectedSize): ByteArray {
        // Fixed-size fields are rejected before decoding if their encoded form is absurdly large.
        if (expectedSize != null) {
            val maximumEncoded = ((expectedSize + 2) / 3) * 4
            if (value.length > maximumEncoded) throw VaultException("Поле $name имеет некорректную длину.")
        } else if (value.length > ((VaultConstraints.MAX_CIPHERTEXT_BYTES + 2) / 3) * 4 + 4) {
            throw VaultException("Поле $name слишком большое.")
        }
        val bytes = try {
            Base64.getDecoder().decode(value)
        } catch (error: IllegalArgumentException) {
            throw VaultException("Поле $name повреждено (ошибка Base64).", error)
        }
        if (expectedSize != null && bytes.size != expectedSize) {
            bytes.fill(0)
            throw VaultException("Поле $name имеет некорректную длину.")
        }
        if (maxSize != null && bytes.size > maxSize) {
            bytes.fill(0)
            throw VaultException("Поле $name слишком большое.")
        }
        return bytes
    }

    private fun readVaultVersion(root: JSONObject): Int {
        // Desktop v2 writes `version`. `formatVersion` is accepted only as a defensive
        // compatibility alias for older experimental builds; it does not alter v2 AAD.
        val primary = root.opt("version")
        val fallback = root.opt("formatVersion")
        val value = if (primary != null && primary !== JSONObject.NULL) primary else fallback
        return when (value) {
            is Int -> value
            is Long -> if (value in Int.MIN_VALUE..Int.MAX_VALUE) value.toInt()
                else throw VaultException("Поле version повреждено.")
            is Number -> value.toInt()
            else -> throw VaultException(
                "Не найден номер версии vault. Убедитесь, что выбран именно файл vault.pnb."
            )
        }
    }

    private fun readRequiredString(root: JSONObject, name: String): String {
        val value = root.opt(name)
        return if (value is String && value.isNotBlank()) value
        else throw VaultException("Поле $name в хранилище отсутствует или повреждено.")
    }

    private fun readRequiredInt(root: JSONObject, name: String): Int {
        val value = root.opt(name)
        return when (value) {
            is Int -> value
            is Long -> if (value in Int.MIN_VALUE..Int.MAX_VALUE) value.toInt() else throw VaultException("Поле $name повреждено.")
            is Number -> value.toInt()
            else -> throw VaultException("Поле $name в хранилище отсутствует или повреждено.")
        }
    }

    private fun readRequiredLong(root: JSONObject, name: String): Long {
        val value = root.opt(name)
        return when (value) {
            is Number -> value.toLong()
            else -> throw VaultException("Поле $name в хранилище отсутствует или повреждено.")
        }
    }

    private fun LegacyEnvelope.clear() {
        salt.fill(0)
        nonce.fill(0)
        ciphertext.fill(0)
        tag.fill(0)
    }

    private fun V2Envelope.clearExceptSaltForTransfer() {
        wrappedKeyNonce.fill(0)
        wrappedKey.fill(0)
        wrappedKeyTag.fill(0)
        nonce.fill(0)
        ciphertext.fill(0)
        tag.fill(0)
        // salt ownership is transferred into the successful OpenedVault. On failure the
        // caller's session is never created, but salt is non-secret and bounded.
    }
}

class VaultException(message: String, cause: Throwable? = null) : Exception(message, cause)
