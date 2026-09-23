package com.geniapassword.mobile.data

import android.content.Context
import android.net.Uri
import java.io.ByteArrayOutputStream
import java.io.Closeable
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.InputStream
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest

class VaultSession internal constructor(
    val data: VaultData,
    internal val vaultKey: ByteArray,
    internal val unlockKey: ByteArray,
    internal val salt: ByteArray,
    internal val kdf: VaultKdfParameters,
    internal var revision: Long,
) : Closeable {
    override fun close() {
        vaultKey.fill(0)
        unlockKey.fill(0)
        salt.fill(0)
        data.entries.clear()
    }
}

class VaultRepository(private val context: Context) {
    private val vaultFile = File(context.filesDir, "vault.pnb")
    private val temporaryFile = File(context.filesDir, "vault.pnb.tmp")
    private val backupManager = VaultBackupManager(File(context.filesDir, "backups"))
    private val syncGuard = VaultSyncGuard(context)

    fun hasVault(): Boolean = vaultFile.isFile

    fun backupCount(): Int = backupManager.listBackups().size

    fun create(masterPassword: CharArray): VaultSession {
        ensureCurrentKdfFitsDevice()
        val salt = VaultCrypto.createSalt()
        val unlockKey = VaultCrypto.deriveCurrentUnlockKey(masterPassword, salt)
        val vaultKey = VaultCrypto.createVaultKey()
        val session = VaultSession(
            data = VaultData(),
            vaultKey = vaultKey,
            unlockKey = unlockKey,
            salt = salt,
            kdf = VaultCrypto.currentKdf,
            revision = 0,
        )
        return try {
            // A newly-created vault is not yet synced with an external copy.
            syncGuard.clear()
            save(session)
            session
        } catch (error: Exception) {
            session.close()
            throw error
        }
    }

    fun unlock(masterPassword: CharArray): VaultSession {
        val bytes = readVaultBytes(vaultFile)
        var opened: VaultCrypto.OpenedVault? = null
        try {
            opened = VaultCrypto.decrypt(bytes, masterPassword, deviceArgon2MemoryLimitKiB())
            val needsMigration = opened.needsMigration
            val needsKdfUpgrade = !needsMigration && VaultCrypto.needsKdfUpgrade(opened.kdf)
            val session = if (needsKdfUpgrade) {
                opened.toStrengthenedSession(masterPassword)
            } else {
                opened.toSession()
            }
            opened = null // ownership of key material moved into the session

            // Legacy v1 and weak v2 KDFs are upgraded only after successful authentication.
            if (needsMigration || needsKdfUpgrade) {
                try {
                    save(session)
                } catch (error: Exception) {
                    session.close()
                    throw error
                }
            }
            return session
        } finally {
            opened?.clearSensitive()
            bytes.fill(0)
        }
    }

    fun unlockWithUnlockKey(unlockKey: ByteArray): VaultSession {
        val bytes = readVaultBytes(vaultFile)
        var opened: VaultCrypto.OpenedVault? = null
        try {
            opened = VaultCrypto.decryptWithUnlockKey(bytes, unlockKey)
            val session = opened.toSession()
            opened = null
            return session
        } finally {
            opened?.clearSensitive()
            bytes.fill(0)
        }
    }

    fun importVault(uri: Uri, masterPassword: CharArray): VaultSession {
        val bytes = context.contentResolver.openInputStream(uri)?.use(::readLimited)
            ?: throw VaultException("Не удалось прочитать выбранный файл.")

        var opened: VaultCrypto.OpenedVault? = null
        try {
            // Fingerprints contain only encrypted bytes + authenticated envelope metadata.
            val incomingFingerprint = fingerprint(bytes)
            opened = VaultCrypto.decrypt(bytes, masterPassword, deviceArgon2MemoryLimitKiB())

            val localFingerprint = if (vaultFile.isFile) fingerprint(vaultFile) else null
            VaultSyncPolicy.validateImport(localFingerprint, incomingFingerprint, syncGuard.lastSynced())

            val isLegacy = opened.needsMigration
            val needsKdfUpgrade = !isLegacy && VaultCrypto.needsKdfUpgrade(opened.kdf)
            val session = if (needsKdfUpgrade) {
                opened.toStrengthenedSession(masterPassword)
            } else {
                opened.toSession()
            }
            opened = null

            try {
                if (isLegacy || needsKdfUpgrade) {
                    // Install only a strong v2 representation. The current local file is backed up
                    // before replacement by writeVaultBytes().
                    save(session)
                    syncGuard.markSynced(fingerprint(vaultFile))
                } else {
                    // Valid strong v2 is copied byte-for-byte: no artificial revision bump.
                    writeVaultBytes(bytes)
                    syncGuard.markSynced(incomingFingerprint)
                }
                return session
            } catch (error: Exception) {
                session.close()
                throw error
            }
        } finally {
            opened?.clearSensitive()
            bytes.fill(0)
        }
    }

    fun save(session: VaultSession) {
        val nextRevision = if (session.revision <= 0L) 1L else session.revision + 1L
        if (nextRevision <= 0L) throw VaultException("Переполнение номера ревизии vault.")
        val savedUtcUnixMilliseconds = System.currentTimeMillis()
        if (savedUtcUnixMilliseconds <= 0L) throw VaultException("Системная дата имеет некорректное значение.")

        val bytes = VaultCrypto.encryptV2(
            data = session.data,
            vaultKey = session.vaultKey,
            unlockKey = session.unlockKey,
            salt = session.salt,
            kdf = session.kdf,
            revision = nextRevision,
            savedUtcUnixMilliseconds = savedUtcUnixMilliseconds,
        )
        try {
            if (bytes.size > VaultConstraints.MAX_VAULT_BYTES) {
                throw VaultException("Хранилище превышает допустимый размер.")
            }
            writeVaultBytes(bytes)
            // Advance the in-memory revision only after the durable replacement succeeded.
            session.revision = nextRevision
        } finally {
            bytes.fill(0)
        }
    }

    fun export(uri: Uri) {
        if (!vaultFile.isFile) throw VaultException("Хранилище ещё не создано.")
        val sourceFingerprint = fingerprint(vaultFile)

        context.contentResolver.openOutputStream(uri, "wt")?.use { output ->
            vaultFile.inputStream().use { input -> input.copyTo(output) }
            output.flush()
        } ?: throw VaultException("Не удалось создать файл резервной копии.")

        // SAF providers normally allow reopening a newly created document. Refuse to report
        // success unless the encrypted export can be read back and verified byte-for-byte.
        val exportedFingerprint = context.contentResolver.openInputStream(uri)?.use { input ->
            val exportedBytes = readLimited(input)
            try {
                fingerprint(exportedBytes)
            } finally {
                exportedBytes.fill(0)
            }
        } ?: throw VaultException("Экспорт записан, но не удалось повторно открыть файл для проверки.")

        if (sourceFingerprint.sha256Hex != exportedFingerprint.sha256Hex) {
            throw VaultException("Проверка экспорта не пройдена: SHA-256 записанного vault не совпадает с исходным.")
        }
        syncGuard.markSynced(sourceFingerprint)
    }

    private fun VaultCrypto.OpenedVault.toSession(): VaultSession = VaultSession(
        data = data,
        vaultKey = vaultKey,
        unlockKey = unlockKey,
        salt = salt,
        kdf = kdf,
        revision = revision,
    )

    private fun VaultCrypto.OpenedVault.toStrengthenedSession(masterPassword: CharArray): VaultSession {
        ensureCurrentKdfFitsDevice()
        val strongerSalt = VaultCrypto.createSalt()
        val strongerUnlockKey = try {
            VaultCrypto.deriveCurrentUnlockKey(masterPassword, strongerSalt)
        } catch (error: Exception) {
            strongerSalt.fill(0)
            throw error
        }
        // Old KDF material is no longer needed after successful derivation.
        unlockKey.fill(0)
        salt.fill(0)
        return VaultSession(
            data = data,
            vaultKey = vaultKey,
            unlockKey = strongerUnlockKey,
            salt = strongerSalt,
            kdf = VaultCrypto.currentKdf,
            revision = revision,
        )
    }

    private fun VaultCrypto.OpenedVault.clearSensitive() {
        vaultKey.fill(0)
        unlockKey.fill(0)
        salt.fill(0)
        data.entries.clear()
    }

    private fun readVaultBytes(file: File): ByteArray {
        if (!file.isFile) throw VaultException("Файл хранилища не найден.")
        if (file.length() !in 1..VaultConstraints.MAX_VAULT_BYTES.toLong()) {
            throw VaultException("Файл хранилища пуст или слишком большой.")
        }
        return file.inputStream().use(::readLimited)
    }

    private fun readLimited(input: InputStream): ByteArray {
        val output = ByteArrayOutputStream(64 * 1024)
        val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
        var total = 0
        try {
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                if (read == 0) continue
                if (total > VaultConstraints.MAX_VAULT_BYTES - read) {
                    throw VaultException("Файл хранилища слишком большой.")
                }
                output.write(buffer, 0, read)
                total += read
            }
            if (total == 0) throw VaultException("Выбранный файл пуст.")
            return output.toByteArray()
        } finally {
            buffer.fill(0)
        }
    }

    private fun writeVaultBytes(bytes: ByteArray) {
        if (bytes.isEmpty() || bytes.size > VaultConstraints.MAX_VAULT_BYTES) {
            throw VaultException("Некорректный размер хранилища.")
        }
        val expectedDigest = sha256(bytes)

        try {
            FileOutputStream(temporaryFile, false).use { stream ->
                stream.write(bytes)
                stream.flush()
                stream.fd.sync()
            }

            if (temporaryFile.length() != bytes.size.toLong()) {
                throw VaultException("Не удалось проверить временный файл хранилища.")
            }
            if (!digestMatches(expectedDigest, temporaryFile)) {
                throw VaultException("SHA-256 временного файла vault не совпадает с данными в памяти.")
            }

            // Mandatory rule: if a verified backup of the current vault cannot be created,
            // the primary file is not replaced.
            if (vaultFile.isFile) backupManager.createBackup(vaultFile)

            try {
                Files.move(
                    temporaryFile.toPath(),
                    vaultFile.toPath(),
                    StandardCopyOption.REPLACE_EXISTING,
                    StandardCopyOption.ATOMIC_MOVE,
                )
            } catch (_: AtomicMoveNotSupportedException) {
                Files.move(
                    temporaryFile.toPath(),
                    vaultFile.toPath(),
                    StandardCopyOption.REPLACE_EXISTING,
                )
            }

            if (!vaultFile.isFile || vaultFile.length() != bytes.size.toLong() ||
                !digestMatches(expectedDigest, vaultFile)
            ) {
                throw VaultException("Финальная проверка сохранённого vault не пройдена.")
            }
        } finally {
            expectedDigest.fill(0)
            temporaryFile.delete()
        }
    }

    private fun fingerprint(file: File): VaultFingerprint {
        val bytes = readVaultBytes(file)
        return try {
            fingerprint(bytes)
        } finally {
            bytes.fill(0)
        }
    }

    private fun fingerprint(bytes: ByteArray): VaultFingerprint {
        val info = VaultCrypto.inspectEnvelope(bytes)
        val digest = sha256(bytes)
        return try {
            VaultFingerprint(
                version = info.version,
                revision = info.revision,
                sha256Hex = digest.joinToString(separator = "") { byte -> "%02x".format(byte.toInt() and 0xff) },
            )
        } finally {
            digest.fill(0)
        }
    }

    private fun digestMatches(expected: ByteArray, file: File): Boolean {
        val actual = sha256(file)
        return try {
            MessageDigest.isEqual(expected, actual)
        } finally {
            actual.fill(0)
        }
    }

    private fun sha256(bytes: ByteArray): ByteArray =
        MessageDigest.getInstance("SHA-256").digest(bytes)

    private fun sha256(file: File): ByteArray {
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
        try {
            FileInputStream(file).use { input ->
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    if (read > 0) digest.update(buffer, 0, read)
                }
            }
            return digest.digest()
        } finally {
            buffer.fill(0)
        }
    }

    private fun ensureCurrentKdfFitsDevice() {
        val limit = deviceArgon2MemoryLimitKiB()
        if (VaultCrypto.currentKdf.memoryKiB > limit) {
            throw VaultException(
                "На этом устройстве безопасный лимит памяти Argon2id — ${limit / 1024} MiB, " +
                    "а GeniaPassword требует ${VaultCrypto.currentKdf.memoryKiB / 1024} MiB. " +
                    "Операция остановлена заранее для защиты от нехватки памяти."
            )
        }
    }

    private fun deviceArgon2MemoryLimitKiB(): Int =
        VaultSecurityPolicy.deviceArgon2MemoryLimitKiB(Runtime.getRuntime().maxMemory())
}
