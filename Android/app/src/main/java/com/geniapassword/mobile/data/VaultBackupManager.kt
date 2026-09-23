package com.geniapassword.mobile.data

import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.nio.charset.StandardCharsets
import java.nio.file.AtomicMoveNotSupportedException
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter

internal class VaultBackupManager(
    private val backupDirectory: File,
    private val maxBackups: Int = DEFAULT_MAX_BACKUPS,
) {
    init {
        require(maxBackups > 0) { "Количество резервных копий должно быть больше нуля." }
    }

    fun createBackup(source: File): File {
        if (!source.isFile) throw VaultException("Файл хранилища для резервной копии не найден.")
        if (source.length() !in 1..VaultConstraints.MAX_VAULT_BYTES.toLong()) {
            throw VaultException("Файл хранилища для резервной копии имеет некорректный размер.")
        }
        if (!backupDirectory.exists() && !backupDirectory.mkdirs()) {
            throw VaultException("Не удалось создать каталог резервных копий.")
        }

        val descriptor = readDescriptor(source)
        val timestamp = BACKUP_TIMESTAMP.format(Instant.now())
        val revisionSuffix = descriptor.revision?.let { "_rev$it" } ?: "_v${descriptor.version}"
        val destination = uniqueDestination("vault_${timestamp}${revisionSuffix}.pnb")
        val temporary = File(backupDirectory, ".${destination.name}.tmp")

        try {
            FileOutputStream(temporary, false).use { output ->
                source.inputStream().use { input -> input.copyTo(output) }
                output.flush()
                output.fd.sync()
            }

            if (temporary.length() != source.length()) {
                throw VaultException("Размер резервной копии не совпадает с исходным файлом.")
            }
            if (!MessageDigest.isEqual(sha256(source), sha256(temporary))) {
                throw VaultException("SHA-256 резервной копии не совпадает с исходным файлом.")
            }

            moveAtomically(temporary, destination)
            trimOldBackups()
            return destination
        } catch (error: VaultException) {
            throw error
        } catch (error: Exception) {
            throw VaultException("Не удалось создать резервную копию хранилища.", error)
        } finally {
            temporary.delete()
        }
    }

    fun listBackups(): List<File> =
        backupDirectory
            .listFiles { file -> file.isFile && file.name.startsWith("vault_") && file.extension == "pnb" }
            ?.sortedByDescending { it.lastModified() }
            .orEmpty()

    private fun trimOldBackups() {
        listBackups()
            .drop(maxBackups)
            .forEach { it.delete() }
    }

    private fun uniqueDestination(baseName: String): File {
        var destination = File(backupDirectory, baseName)
        var suffix = 1
        while (destination.exists()) {
            destination = File(backupDirectory, baseName.removeSuffix(".pnb") + "_$suffix.pnb")
            suffix++
        }
        return destination
    }

    private fun readDescriptor(source: File): VaultDescriptor {
        return try {
            val root = JSONObject(source.readText(StandardCharsets.UTF_8))
            VaultDescriptor(
                version = root.optInt("version", 0),
                revision = if (root.has("revision")) root.optLong("revision") else null,
            )
        } catch (_: Exception) {
            VaultDescriptor(version = 0, revision = null)
        }
    }

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

    private fun moveAtomically(source: File, destination: File) {
        try {
            Files.move(
                source.toPath(),
                destination.toPath(),
                StandardCopyOption.ATOMIC_MOVE,
            )
        } catch (_: AtomicMoveNotSupportedException) {
            Files.move(source.toPath(), destination.toPath())
        }
    }

    private data class VaultDescriptor(
        val version: Int,
        val revision: Long?,
    )

    private companion object {
        const val DEFAULT_MAX_BACKUPS = 10

        val BACKUP_TIMESTAMP: DateTimeFormatter =
            DateTimeFormatter.ofPattern("yyyyMMdd_HHmmss_SSS").withZone(ZoneOffset.UTC)
    }
}
