package com.geniapassword.mobile.data

import org.junit.Assert.assertEquals
import org.junit.Test

class VaultArgon2InteropTest {
    @Test
    fun argon2idMatchesIndependentReferenceVector() {
        val password = "interop-Päss-密码".toCharArray()
        val salt = ByteArray(16) { it.toByte() }
        val parameters = VaultKdfParameters(
            name = "Argon2id",
            iterations = 3,
            memoryKiB = 65_536,
            parallelism = 2,
        )

        val key = try {
            VaultCrypto.deriveArgon2id(password, salt, parameters)
        } finally {
            password.fill('\u0000')
            salt.fill(0)
        }

        try {
            assertEquals(
                "4bfacf618896bb3bd5960ab187d8898cc025d9d443df3863edd7e8d2a945cfb1",
                key.joinToString("") { byte -> "%02x".format(byte.toInt() and 0xff) },
            )
        } finally {
            key.fill(0)
        }
    }
}
