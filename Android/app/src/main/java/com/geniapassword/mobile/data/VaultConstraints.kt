package com.geniapassword.mobile.data

internal object VaultConstraints {
    const val MAX_VAULT_BYTES = 8 * 1024 * 1024
    const val MAX_CIPHERTEXT_BYTES = 6 * 1024 * 1024
    const val MAX_ENTRIES = 5_000
    const val MAX_CUSTOM_FIELDS_PER_ENTRY = 100
    const val MAX_PASSWORD_HISTORY_PER_ENTRY = 100
    const val MAX_UNKNOWN_PROPERTIES_PER_OBJECT = 128

    const val MAX_ID_CHARS = 128
    const val MAX_TITLE_CHARS = 512
    const val MAX_WEBSITE_CHARS = 2_048
    const val MAX_USERNAME_CHARS = 2_048
    const val MAX_PASSWORD_CHARS = 4_096
    const val MAX_NOTES_CHARS = 65_536
    const val MAX_FIELD_NAME_CHARS = 512
    const val MAX_FIELD_VALUE_CHARS = 65_536
    const val MAX_UPDATED_UTC_CHARS = 64

    // Unknown desktop fields are preserved for round-trip compatibility, but bounded separately
    // so an authenticated yet hostile vault cannot retain megabytes of opaque managed Strings.
    const val MAX_UNKNOWN_ROOT_JSON_CHARS = 256 * 1024
    const val MAX_UNKNOWN_ENTRY_JSON_CHARS = 128 * 1024
    const val MAX_UNKNOWN_FIELD_JSON_CHARS = 16 * 1024
    const val MAX_PASSWORD_HISTORY_JSON_CHARS = 512 * 1024
}
