package com.geniapassword.mobile

import android.content.Context
import android.net.Uri
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.geniapassword.mobile.data.VaultEntry
import com.geniapassword.mobile.data.VaultRepository
import com.geniapassword.mobile.data.VaultSession
import com.geniapassword.mobile.security.PasswordGenerator
import java.time.Instant
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.withContext

sealed interface AccessState {
    data object NeedsCreation : AccessState
    data object Locked : AccessState
    data class Open(val session: VaultSession) : AccessState
}

class VaultController(context: Context) {
    private val repository = VaultRepository(context.applicationContext)
    private val operationMutex = Mutex()

    var accessState by mutableStateOf<AccessState>(
        if (repository.hasVault()) AccessState.Locked else AccessState.NeedsCreation,
    )
        private set

    var revision by mutableIntStateOf(0)
        private set

    var message by mutableStateOf<String?>(null)
        private set

    var busy by mutableStateOf(false)
        private set

    val entries: List<VaultEntry>
        get() = (accessState as? AccessState.Open)?.session?.data?.entries.orEmpty()

    suspend fun create(password: String, confirmation: String): Boolean {
        if (password.length < 8) return fail("Мастер-пароль должен содержать не менее 8 символов.")
        if (password != confirmation) return fail("Введённые пароли не совпадают.")
        return runVaultAction("Хранилище создано в формате Vault v2") {
            val chars = password.toCharArray()
            try {
                val session = withContext(Dispatchers.Default) { repository.create(chars) }
                replaceSession(session)
            } finally {
                chars.fill('\u0000')
            }
        }
    }

    suspend fun unlock(password: String): Boolean = runVaultAction(null) {
        val chars = password.toCharArray()
        try {
            val session = withContext(Dispatchers.Default) { repository.unlock(chars) }
            replaceSession(session)
        } finally {
            chars.fill('\u0000')
        }
    }

    suspend fun unlockWithUnlockKey(unlockKey: ByteArray): Boolean = runVaultAction(null) {
        val session = withContext(Dispatchers.Default) { repository.unlockWithUnlockKey(unlockKey) }
        replaceSession(session)
    }

    /**
     * Returns null on success or an error string displayed inside the import dialog.
     */
    suspend fun importVault(uri: Uri, password: String): String? {
        if (!operationMutex.tryLock()) return "Другая операция с хранилищем ещё выполняется."
        busy = true
        return try {
            val chars = password.toCharArray()
            try {
                val session = withContext(Dispatchers.Default) { repository.importVault(uri, chars) }
                replaceSession(session)
                message = "Vault v2 импортирован"
                null
            } finally {
                chars.fill('\u0000')
            }
        } catch (error: Exception) {
            error.message ?: "Не удалось импортировать выбранное хранилище."
        } finally {
            busy = false
            operationMutex.unlock()
        }
    }

    suspend fun export(uri: Uri): Boolean = runVaultAction("Зашифрованная копия vault.pnb сохранена") {
        withContext(Dispatchers.IO) { repository.export(uri) }
    }

    suspend fun saveEntry(entry: VaultEntry): Boolean = runVaultAction("Запись сохранена") {
        val session = requireSession()
        val index = session.data.entries.indexOfFirst { it.id == entry.id }
        val previousEntry = index.takeIf { it >= 0 }?.let(session.data.entries::get)
        val now = Instant.now().toString()
        val passwordChanged = previousEntry != null && previousEntry.password != entry.password
        val replacement = entry.copy(
            updatedUtc = now,
            passwordUpdatedUtc = when {
                passwordChanged -> now
                previousEntry != null -> previousEntry.passwordUpdatedUtc
                else -> entry.passwordUpdatedUtc.ifBlank { now }
            },
            // Carry only bounded unknown desktop fields/history forward. Known secrets are
            // stored once in the typed model instead of being duplicated in preserved JSON.
            passwordHistoryJson = previousEntry?.passwordHistoryJson ?: entry.passwordHistoryJson,
            unknownJson = previousEntry?.unknownJson ?: entry.unknownJson,
        )

        // Existing desktop passwordHistory remains opaque and bounded. Android still does not
        // invent history-item objects until the shared Windows/Android schema is formalized.
        val previous = session.data.entries.toList()
        try {
            if (index >= 0) session.data.entries[index] = replacement else session.data.entries += replacement
            withContext(Dispatchers.Default) { repository.save(session) }
            revision++
        } catch (error: Exception) {
            session.data.entries.clear()
            session.data.entries.addAll(previous)
            throw error
        }
    }

    suspend fun deleteEntry(id: String): Boolean = runVaultAction("Запись удалена") {
        val session = requireSession()
        val previous = session.data.entries.toList()
        session.data.entries.removeAll { it.id == id }
        try {
            withContext(Dispatchers.Default) { repository.save(session) }
            revision++
        } catch (error: Exception) {
            session.data.entries.clear()
            session.data.entries.addAll(previous)
            throw error
        }
    }

    fun lock() {
        if (busy || operationMutex.isLocked) return
        (accessState as? AccessState.Open)?.session?.close()
        accessState = if (repository.hasVault()) AccessState.Locked else AccessState.NeedsCreation
        revision++
    }

    fun consumeMessage() {
        message = null
    }

    fun takeMessage(): String? {
        val current = message
        message = null
        return current
    }

    fun notify(text: String) {
        message = text
    }

    /**
     * Biometric quick unlock stores only the Argon2id-derived UnlockKey. The VaultKey stays
     * session-only; on each save the same UnlockKey re-wraps it using the new revision AAD.
     */
    fun currentUnlockKeyCopy(): ByteArray? =
        (accessState as? AccessState.Open)?.session?.unlockKey?.copyOf()

    fun generatePassword(
        length: Int = 16,
        useUpper: Boolean = true,
        useDigits: Boolean = true,
        useSymbols: Boolean = true,
        excludeAmbiguous: Boolean = false,
    ): String = PasswordGenerator.generate(
        length = length,
        useUpper = useUpper,
        useDigits = useDigits,
        useSymbols = useSymbols,
        excludeAmbiguous = excludeAmbiguous,
    )

    val currentVaultRevision: Long?
        get() = (accessState as? AccessState.Open)?.session?.revision

    fun localBackupCount(): Int = repository.backupCount()

    private fun replaceSession(session: VaultSession) {
        (accessState as? AccessState.Open)?.session?.close()
        accessState = AccessState.Open(session)
        revision++
    }

    private fun requireSession(): VaultSession =
        (accessState as? AccessState.Open)?.session ?: error("Хранилище заблокировано.")

    private fun fail(text: String): Boolean {
        message = text
        return false
    }

    private suspend fun runVaultAction(
        successMessage: String?,
        action: suspend () -> Unit,
    ): Boolean {
        if (!operationMutex.tryLock()) {
            message = "Другая операция с хранилищем ещё выполняется."
            return false
        }
        busy = true
        return try {
            action()
            message = successMessage
            true
        } catch (error: Exception) {
            message = error.message ?: "Неизвестная ошибка"
            false
        } finally {
            busy = false
            operationMutex.unlock()
        }
    }
}
