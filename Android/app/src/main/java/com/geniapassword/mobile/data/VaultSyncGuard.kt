package com.geniapassword.mobile.data

import android.content.Context

internal class VaultSyncGuard(context: Context) {
    private val preferences = context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    fun lastSynced(): VaultFingerprint? {
        val hash = preferences.getString(KEY_HASH, null)?.takeIf { it.length == 64 } ?: return null
        val version = preferences.getInt(KEY_VERSION, 0)
        val revision = if (preferences.getBoolean(KEY_HAS_REVISION, false)) {
            preferences.getLong(KEY_REVISION, 0L)
        } else {
            null
        }
        return VaultFingerprint(version = version, revision = revision, sha256Hex = hash)
    }

    fun markSynced(fingerprint: VaultFingerprint) {
        preferences.edit()
            .putString(KEY_HASH, fingerprint.sha256Hex)
            .putInt(KEY_VERSION, fingerprint.version)
            .putBoolean(KEY_HAS_REVISION, fingerprint.revision != null)
            .putLong(KEY_REVISION, fingerprint.revision ?: 0L)
            .apply()
    }

    fun clear() {
        preferences.edit().clear().apply()
    }

    private companion object {
        const val PREFERENCES_NAME = "vault_sync_guard_v1"
        const val KEY_HASH = "sha256"
        const val KEY_VERSION = "version"
        const val KEY_HAS_REVISION = "has_revision"
        const val KEY_REVISION = "revision"
    }
}
