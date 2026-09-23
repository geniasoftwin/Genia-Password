package com.geniapassword.mobile.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class VaultSecurityPolicyTest {
    @Test
    fun weakKdfIsMarkedForUpgrade() {
        assertTrue(
            VaultSecurityPolicy.needsKdfUpgrade(
                VaultKdfParameters("Argon2id", iterations = 1, memoryKiB = 8 * 1024, parallelism = 1),
            ),
        )
        assertFalse(VaultSecurityPolicy.needsKdfUpgrade(VaultCrypto.currentKdf))
    }

    @Test
    fun deviceArgonBudgetIsBounded() {
        assertEquals(43_690, VaultSecurityPolicy.deviceArgon2MemoryLimitKiB(128L * 1024 * 1024))
        assertEquals(128 * 1024, VaultSecurityPolicy.deviceArgon2MemoryLimitKiB(384L * 1024 * 1024))
        assertEquals(256 * 1024, VaultSecurityPolicy.deviceArgon2MemoryLimitKiB(2L * 1024 * 1024 * 1024))
    }

    @Test(expected = VaultException::class)
    fun lowerRevisionIsRejected() {
        VaultSyncPolicy.validateImport(
            local = VaultFingerprint(2, 10, "a".repeat(64)),
            incoming = VaultFingerprint(2, 9, "b".repeat(64)),
            lastSynced = null,
        )
    }

    @Test(expected = VaultException::class)
    fun sameRevisionDifferentHashIsRejected() {
        VaultSyncPolicy.validateImport(
            local = VaultFingerprint(2, 10, "a".repeat(64)),
            incoming = VaultFingerprint(2, 10, "b".repeat(64)),
            lastSynced = null,
        )
    }

    @Test(expected = VaultException::class)
    fun divergentBranchesAreRejected() {
        val base = VaultFingerprint(2, 10, "0".repeat(64))
        VaultSyncPolicy.validateImport(
            local = VaultFingerprint(2, 11, "1".repeat(64)),
            incoming = VaultFingerprint(2, 12, "2".repeat(64)),
            lastSynced = base,
        )
    }

    @Test
    fun cleanLocalAllowsNewIncomingBranch() {
        val base = VaultFingerprint(2, 10, "0".repeat(64))
        VaultSyncPolicy.validateImport(
            local = base,
            incoming = VaultFingerprint(2, 11, "1".repeat(64)),
            lastSynced = base,
        )
    }
}
