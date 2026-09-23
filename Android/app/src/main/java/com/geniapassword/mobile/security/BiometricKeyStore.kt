package com.geniapassword.mobile.security

import android.content.Context
import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import androidx.biometric.BiometricManager
import java.security.KeyStore
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class BiometricKeyStore(context: Context) {
    private val appContext = context.applicationContext
    private val preferences = appContext.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    init {
        // Beta3 stored the data VaultKey. Vault v2 must never use that credential because
        // quick unlock needs the password-derived UnlockKey in order to unwrap the current
        // VaultKey with revision-bound AAD. Remove the old credential on first Beta4 start.
        clearLegacyBeta3Credential()
    }

    fun isSupported(): Boolean {
        val manager = BiometricManager.from(appContext)
        val authenticators = BiometricManager.Authenticators.BIOMETRIC_STRONG
        return manager.canAuthenticate(authenticators) == BiometricManager.BIOMETRIC_SUCCESS
    }

    fun hasCredential(): Boolean = runCatching {
        preferences.contains(KEY_WRAPPED_KEY) &&
            preferences.contains(KEY_IV) &&
            keyStore().containsAlias(KEY_ALIAS)
    }.getOrDefault(false)

    fun prepareEnrollmentCipher(): Cipher {
        clear()
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, ANDROID_KEYSTORE)
        val builder = KeyGenParameterSpec.Builder(
            KEY_ALIAS,
            KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT,
        )
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .setUserAuthenticationRequired(true)
            .setInvalidatedByBiometricEnrollment(true)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            builder.setUserAuthenticationParameters(
                0,
                KeyProperties.AUTH_BIOMETRIC_STRONG,
            )
        } else {
            @Suppress("DEPRECATION")
            builder.setUserAuthenticationValidityDurationSeconds(-1)
        }

        generator.init(builder.build())
        val key = generator.generateKey()
        return Cipher.getInstance(TRANSFORMATION).apply {
            init(Cipher.ENCRYPT_MODE, key)
        }
    }

    fun completeEnrollment(cipher: Cipher, unlockKey: ByteArray) {
        require(unlockKey.size == 32) { "Некорректный ключ разблокировки." }
        val wrappedKey = cipher.doFinal(unlockKey)
        try {
            val saved = preferences.edit()
                .putString(KEY_WRAPPED_KEY, Base64.getEncoder().encodeToString(wrappedKey))
                .putString(KEY_IV, Base64.getEncoder().encodeToString(cipher.iv))
                .commit()
            check(saved) { "Не удалось сохранить параметры биометрии." }
        } finally {
            wrappedKey.fill(0)
        }
    }

    fun prepareUnlockCipher(): Cipher {
        val key = keyStore().getKey(KEY_ALIAS, null) as? SecretKey
            ?: throw IllegalStateException("Ключ биометрии не найден.")
        val iv = preferences.getString(KEY_IV, null)?.let(Base64.getDecoder()::decode)
            ?: throw IllegalStateException("Параметры биометрии не найдены.")
        return try {
            Cipher.getInstance(TRANSFORMATION).apply {
                init(Cipher.DECRYPT_MODE, key, GCMParameterSpec(128, iv))
            }
        } finally {
            iv.fill(0)
        }
    }

    fun unwrapUnlockKey(cipher: Cipher): ByteArray {
        val wrappedKey = preferences.getString(KEY_WRAPPED_KEY, null)?.let(Base64.getDecoder()::decode)
            ?: throw IllegalStateException("Зашифрованный ключ не найден.")
        return try {
            cipher.doFinal(wrappedKey).also {
                if (it.size != 32) {
                    it.fill(0)
                    throw IllegalStateException("Биометрический ключ имеет неверную длину.")
                }
            }
        } finally {
            wrappedKey.fill(0)
        }
    }

    fun clear() {
        preferences.edit().clear().commit()
        runCatching {
            val store = keyStore()
            if (store.containsAlias(KEY_ALIAS)) store.deleteEntry(KEY_ALIAS)
        }
    }

    private fun clearLegacyBeta3Credential() {
        runCatching {
            appContext.getSharedPreferences(LEGACY_PREFERENCES_NAME, Context.MODE_PRIVATE)
                .edit()
                .clear()
                .commit()
        }
        runCatching {
            val store = keyStore()
            if (store.containsAlias(LEGACY_KEY_ALIAS)) store.deleteEntry(LEGACY_KEY_ALIAS)
        }
    }

    private fun keyStore(): KeyStore = KeyStore.getInstance(ANDROID_KEYSTORE).apply { load(null) }

    private companion object {
        const val ANDROID_KEYSTORE = "AndroidKeyStore"
        const val KEY_ALIAS = "GeniaPassword.BiometricUnlockKey.v2"
        const val PREFERENCES_NAME = "genia_biometric_v2"
        const val KEY_WRAPPED_KEY = "wrapped_unlock_key"
        const val KEY_IV = "wrapped_unlock_key_iv"

        const val LEGACY_KEY_ALIAS = "GeniaPassword.BiometricVaultKey"
        const val LEGACY_PREFERENCES_NAME = "genia_biometric"

        const val TRANSFORMATION = "AES/GCM/NoPadding"
    }
}
