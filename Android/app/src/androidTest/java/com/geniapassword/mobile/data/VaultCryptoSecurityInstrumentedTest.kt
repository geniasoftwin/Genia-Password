package com.geniapassword.mobile.data

import androidx.test.ext.junit.runners.AndroidJUnit4
import java.nio.charset.StandardCharsets
import java.util.Base64
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class VaultCryptoSecurityInstrumentedTest {
    @Test
    fun currentV2RoundTripAndTamperDetection() {
        val password = "correct horse battery staple".toCharArray()
        val salt = VaultCrypto.createSalt()
        val unlockKey = VaultCrypto.deriveCurrentUnlockKey(password, salt)
        val vaultKey = VaultCrypto.createVaultKey()
        val data = VaultData(
            mutableListOf(
                VaultEntry(title = "Test", userName = "user", password = "secret"),
            ),
        )
        val encrypted = VaultCrypto.encryptV2(
            data = data,
            vaultKey = vaultKey,
            unlockKey = unlockKey,
            salt = salt,
            kdf = VaultCrypto.currentKdf,
            revision = 7,
            savedUtcUnixMilliseconds = 1_800_000_000_000L,
        )
        try {
            val opened = VaultCrypto.decrypt(encrypted, password)
            try {
                assertEquals("secret", opened.data.entries.single().password)
                assertEquals(7L, opened.revision)
            } finally {
                opened.vaultKey.fill(0)
                opened.unlockKey.fill(0)
                opened.salt.fill(0)
                opened.data.entries.clear()
            }

            assertTamperRejected(encrypted, password) { root ->
                root.put("revision", root.getLong("revision") + 1L)
            }
            assertTamperRejected(encrypted, password) { root ->
                val bytes = Base64.getDecoder().decode(root.getString("tag"))
                bytes[0] = (bytes[0].toInt() xor 1).toByte()
                root.put("tag", Base64.getEncoder().encodeToString(bytes))
                bytes.fill(0)
            }
            assertTamperRejected(encrypted, password) { root ->
                val bytes = Base64.getDecoder().decode(root.getString("ciphertext"))
                if (bytes.isNotEmpty()) bytes[0] = (bytes[0].toInt() xor 1).toByte()
                root.put("ciphertext", Base64.getEncoder().encodeToString(bytes))
                bytes.fill(0)
            }
        } finally {
            password.fill('\u0000')
            salt.fill(0)
            unlockKey.fill(0)
            vaultKey.fill(0)
            encrypted.fill(0)
        }
    }

    @Test
    fun unknownDesktopFieldsRoundTripWithoutDuplicatingKnownCurrentPassword() {
        val fixture = """{"entries":[{"id":"id-1","title":"Desktop","website":"","userName":"u","password":"current-secret","notes":"","customFields":[{"name":"OTP","value":"123","isSecret":true,"desktopOnly":"keep"}],"updatedUtc":"2026-08-27T00:00:00Z","passwordUpdatedUtc":"2026-08-27T00:00:00Z","passwordHistory":[{"password":"old-secret","futureField":42}],"futureEntryField":{"x":1}}],"futureRootField":"keep-root"}"""
        val bytes = fixture.toByteArray(StandardCharsets.UTF_8)
        val decoded = VaultJson.decode(bytes)
        try {
            val entry = decoded.entries.single()
            assertEquals("current-secret", entry.password)
            assertFalse(entry.unknownJson.orEmpty().contains("current-secret"))
            assertFalse(entry.unknownJson.orEmpty().contains("\"password\""))
            assertTrue(entry.passwordHistoryJson.orEmpty().contains("old-secret"))
            assertTrue(entry.unknownJson.orEmpty().contains("futureEntryField"))
            assertTrue(decoded.unknownRootJson.orEmpty().contains("futureRootField"))
            assertTrue(entry.customFields.single().unknownJson.orEmpty().contains("desktopOnly"))

            val encoded = VaultJson.encode(decoded)
            try {
                val root = JSONObject(String(encoded, StandardCharsets.UTF_8))
                assertEquals("keep-root", root.getString("futureRootField"))
                val item = root.getJSONArray("entries").getJSONObject(0)
                assertNotNull(item.getJSONObject("futureEntryField"))
                assertEquals("old-secret", item.getJSONArray("passwordHistory").getJSONObject(0).getString("password"))
                assertEquals("keep", item.getJSONArray("customFields").getJSONObject(0).getString("desktopOnly"))
            } finally {
                encoded.fill(0)
            }
        } finally {
            bytes.fill(0)
            decoded.entries.clear()
        }
    }

    private fun assertTamperRejected(
        original: ByteArray,
        password: CharArray,
        mutate: (JSONObject) -> Unit,
    ) {
        val root = JSONObject(String(original, StandardCharsets.UTF_8))
        mutate(root)
        val tampered = root.toString().toByteArray(StandardCharsets.UTF_8)
        try {
            var rejected = false
            try {
                val opened = VaultCrypto.decrypt(tampered, password)
                opened.vaultKey.fill(0)
                opened.unlockKey.fill(0)
                opened.salt.fill(0)
                opened.data.entries.clear()
            } catch (_: VaultException) {
                rejected = true
            }
            assertTrue("Подменённый Vault v2 должен быть отклонён.", rejected)
        } finally {
            tampered.fill(0)
        }
    }
}
