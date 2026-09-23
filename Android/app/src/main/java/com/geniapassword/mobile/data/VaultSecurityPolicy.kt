package com.geniapassword.mobile.data

import kotlin.math.min

internal object VaultSecurityPolicy {
    const val HARD_MAX_ARGON2_MEMORY_KIB = 256 * 1024
    private const val MIN_DEVICE_ARGON2_BUDGET_KIB = 8 * 1024

    fun needsKdfUpgrade(kdf: VaultKdfParameters): Boolean =
        kdf.iterations < VaultCrypto.DEFAULT_ARGON2_ITERATIONS ||
            kdf.memoryKiB < VaultCrypto.DEFAULT_ARGON2_MEMORY_KIB

    /**
     * Argon2 allocates a large native/managed working area. Keep untrusted envelopes to roughly
     * one third of the VM heap. On unusually memory-constrained devices the budget may be below
     * GeniaPassword's normal 64 MiB profile; repository code then fails safely before starting a
     * 64 MiB derivation instead of risking an OOM.
     */
    fun deviceArgon2MemoryLimitKiB(maxHeapBytes: Long): Int {
        if (maxHeapBytes <= 0L) return MIN_DEVICE_ARGON2_BUDGET_KIB
        val oneThirdKiB = (maxHeapBytes / 3L / 1024L).coerceAtMost(Int.MAX_VALUE.toLong()).toInt()
        return min(
            HARD_MAX_ARGON2_MEMORY_KIB,
            oneThirdKiB.coerceAtLeast(MIN_DEVICE_ARGON2_BUDGET_KIB),
        )
    }
}

internal data class VaultFingerprint(
    val version: Int,
    val revision: Long?,
    val sha256Hex: String,
)

internal object VaultSyncPolicy {
    fun validateImport(
        local: VaultFingerprint?,
        incoming: VaultFingerprint,
        lastSynced: VaultFingerprint?,
    ) {
        if (local == null || local.sha256Hex == incoming.sha256Hex) return

        if (lastSynced != null) {
            val localChanged = local.sha256Hex != lastSynced.sha256Hex
            val incomingChanged = incoming.sha256Hex != lastSynced.sha256Hex
            if (localChanged && incomingChanged) {
                throw VaultException(
                    "Обнаружен конфликт: после последнего обмена изменились и локальный, и импортируемый vault. " +
                        "Автоматическая перезапись заблокирована. Сохраните обе копии и выберите актуальную вручную."
                )
            }
            if (localChanged && !incomingChanged) {
                throw VaultException(
                    "Импортируемый vault не содержит локальные изменения после последнего экспорта. " +
                        "Импорт заблокирован как возможный откат."
                )
            }
        }

        val localRevision = local.revision
        val incomingRevision = incoming.revision
        if (localRevision != null && incomingRevision != null) {
            if (incomingRevision < localRevision) {
                throw VaultException(
                    "Импорт заблокирован: revision импортируемого vault ($incomingRevision) меньше локального ($localRevision)."
                )
            }
            if (incomingRevision == localRevision) {
                throw VaultException(
                    "Конфликт vault: одинаковый revision $incomingRevision, но содержимое файлов различается. " +
                        "Автоматическая перезапись запрещена."
                )
            }
        }
    }
}
