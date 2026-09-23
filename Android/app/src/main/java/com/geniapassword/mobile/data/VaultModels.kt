package com.geniapassword.mobile.data

import org.json.JSONArray
import org.json.JSONObject
import java.nio.charset.StandardCharsets
import java.time.Instant
import java.util.UUID

data class CustomField(
    val name: String = "",
    val value: String = "",
    val isSecret: Boolean = false,
    /**
     * Only unknown desktop properties are retained here. Known values are stored once in the
     * typed model so current secrets are not duplicated in a full preserved JSON snapshot.
     */
    internal val unknownJson: String? = null,
)

data class VaultEntry(
    val id: String = UUID.randomUUID().toString(),
    val title: String = "",
    val website: String = "",
    val userName: String = "",
    val password: String = "",
    val notes: String = "",
    val customFields: List<CustomField> = emptyList(),
    val updatedUtc: String = Instant.now().toString(),
    val passwordUpdatedUtc: String = updatedUtc,
    /** Existing desktop passwordHistory is kept separately and bounded. */
    internal val passwordHistoryJson: String? = null,
    /** Only unknown entry properties; known password/login/etc. are deliberately excluded. */
    internal val unknownJson: String? = null,
)

data class VaultData(
    val entries: MutableList<VaultEntry> = mutableListOf(),
    /** Only unknown root properties; the entries array is deliberately excluded. */
    internal val unknownRootJson: String? = null,
)

internal object VaultJson {
    private val ROOT_KNOWN_KEYS = setOf("entries")
    private val ENTRY_KNOWN_KEYS = setOf(
        "id", "title", "website", "userName", "password", "notes", "customFields",
        "updatedUtc", "passwordUpdatedUtc", "passwordHistory",
    )
    private val CUSTOM_FIELD_KNOWN_KEYS = setOf("name", "value", "isSecret")

    fun encode(data: VaultData): ByteArray {
        validateData(data)
        val root = parsePreservedObject(data.unknownRootJson)
        val entries = JSONArray()
        data.entries.forEach { entry ->
            val item = parsePreservedObject(entry.unknownJson)
                .put("id", entry.id)
                .put("title", entry.title)
                .put("website", entry.website)
                .put("userName", entry.userName)
                .put("password", entry.password)
                .put("notes", entry.notes)
                .put("updatedUtc", entry.updatedUtc)
                .put("passwordUpdatedUtc", entry.passwordUpdatedUtc)

            if (!entry.passwordHistoryJson.isNullOrBlank()) {
                item.put("passwordHistory", parsePreservedArray(entry.passwordHistoryJson))
            }

            val fields = JSONArray()
            entry.customFields.forEach { field ->
                fields.put(
                    parsePreservedObject(field.unknownJson)
                        .put("name", field.name)
                        .put("value", field.value)
                        .put("isSecret", field.isSecret),
                )
            }
            item.put("customFields", fields)
            entries.put(item)
        }
        root.put("entries", entries)
        return root.toString().toByteArray(StandardCharsets.UTF_8)
    }

    fun decode(bytes: ByteArray): VaultData {
        if (bytes.size > VaultConstraints.MAX_CIPHERTEXT_BYTES) {
            throw VaultException("Расшифрованное хранилище слишком большое.")
        }

        val plaintextJson = try {
            String(bytes, StandardCharsets.UTF_8)
        } catch (error: Exception) {
            throw VaultException("Расшифрованные данные хранилища повреждены.", error)
        }
        val root = try {
            JSONObject(plaintextJson)
        } catch (error: Exception) {
            throw VaultException("Расшифрованные данные хранилища повреждены.", error)
        }

        val rawEntries = root.opt("entries")
        val array = when (rawEntries) {
            null, JSONObject.NULL -> JSONArray()
            is JSONArray -> rawEntries
            else -> throw VaultException("Поле entries имеет неверный формат.")
        }
        if (array.length() > VaultConstraints.MAX_ENTRIES) {
            throw VaultException("В хранилище слишком много записей.")
        }

        val entries = mutableListOf<VaultEntry>()
        val seenIds = HashSet<String>(array.length())

        for (index in 0 until array.length()) {
            val item = array.opt(index) as? JSONObject
                ?: throw VaultException("Запись #${index + 1} имеет неверный формат.")

            val rawFields = item.opt("customFields")
            val fieldsArray = when (rawFields) {
                null, JSONObject.NULL -> JSONArray()
                is JSONArray -> rawFields
                else -> throw VaultException("Дополнительные поля записи #${index + 1} повреждены.")
            }
            if (fieldsArray.length() > VaultConstraints.MAX_CUSTOM_FIELDS_PER_ENTRY) {
                throw VaultException("В одной записи слишком много дополнительных полей.")
            }

            val historyJson = when (val history = item.opt("passwordHistory")) {
                null, JSONObject.NULL -> null
                is JSONArray -> {
                    if (history.length() > VaultConstraints.MAX_PASSWORD_HISTORY_PER_ENTRY) {
                        throw VaultException("В одной записи слишком большая история паролей.")
                    }
                    history.toString().also {
                        validateLength(it, VaultConstraints.MAX_PASSWORD_HISTORY_JSON_CHARS, "История паролей")
                    }
                }
                else -> throw VaultException("История паролей имеет неверный формат.")
            }

            val fields = mutableListOf<CustomField>()
            for (fieldIndex in 0 until fieldsArray.length()) {
                val field = fieldsArray.opt(fieldIndex) as? JSONObject
                    ?: throw VaultException("Дополнительное поле имеет неверный формат.")
                fields += CustomField(
                    name = readString(field, "name", VaultConstraints.MAX_FIELD_NAME_CHARS),
                    value = readString(field, "value", VaultConstraints.MAX_FIELD_VALUE_CHARS),
                    isSecret = readBoolean(field, "isSecret"),
                    unknownJson = extractUnknownJson(
                        field,
                        CUSTOM_FIELD_KNOWN_KEYS,
                        VaultConstraints.MAX_UNKNOWN_FIELD_JSON_CHARS,
                        "Неизвестные свойства дополнительного поля",
                    ),
                )
            }

            val id = readString(item, "id", VaultConstraints.MAX_ID_CHARS)
                .ifBlank { UUID.randomUUID().toString() }
            if (!seenIds.add(id)) {
                throw VaultException("В хранилище обнаружены повторяющиеся идентификаторы записей.")
            }

            val updatedUtc = readString(item, "updatedUtc", VaultConstraints.MAX_UPDATED_UTC_CHARS)
                .ifBlank { Instant.now().toString() }
            val passwordUpdatedUtc = readString(item, "passwordUpdatedUtc", VaultConstraints.MAX_UPDATED_UTC_CHARS)
                .ifBlank { updatedUtc }

            entries += VaultEntry(
                id = id,
                title = readString(item, "title", VaultConstraints.MAX_TITLE_CHARS),
                website = readString(item, "website", VaultConstraints.MAX_WEBSITE_CHARS),
                userName = readString(item, "userName", VaultConstraints.MAX_USERNAME_CHARS),
                password = readString(item, "password", VaultConstraints.MAX_PASSWORD_CHARS),
                notes = normalizeLineEndings(readString(item, "notes", VaultConstraints.MAX_NOTES_CHARS)),
                customFields = fields,
                updatedUtc = updatedUtc,
                passwordUpdatedUtc = passwordUpdatedUtc,
                passwordHistoryJson = historyJson,
                unknownJson = extractUnknownJson(
                    item,
                    ENTRY_KNOWN_KEYS,
                    VaultConstraints.MAX_UNKNOWN_ENTRY_JSON_CHARS,
                    "Неизвестные свойства записи",
                ),
            )
        }

        return VaultData(
            entries = entries,
            unknownRootJson = extractUnknownJson(
                root,
                ROOT_KNOWN_KEYS,
                VaultConstraints.MAX_UNKNOWN_ROOT_JSON_CHARS,
                "Неизвестные свойства хранилища",
            ),
        )
    }

    private fun validateData(data: VaultData) {
        if (data.entries.size > VaultConstraints.MAX_ENTRIES) {
            throw VaultException("В хранилище слишком много записей.")
        }
        data.unknownRootJson?.let {
            validateLength(it, VaultConstraints.MAX_UNKNOWN_ROOT_JSON_CHARS, "Неизвестные свойства хранилища")
        }
        val seenIds = HashSet<String>(data.entries.size)
        data.entries.forEach { entry ->
            validateLength(entry.id, VaultConstraints.MAX_ID_CHARS, "Идентификатор записи")
            if (!seenIds.add(entry.id)) throw VaultException("Обнаружены повторяющиеся идентификаторы записей.")
            validateLength(entry.title, VaultConstraints.MAX_TITLE_CHARS, "Название")
            validateLength(entry.website, VaultConstraints.MAX_WEBSITE_CHARS, "Сайт")
            validateLength(entry.userName, VaultConstraints.MAX_USERNAME_CHARS, "Логин")
            validateLength(entry.password, VaultConstraints.MAX_PASSWORD_CHARS, "Пароль")
            validateLength(entry.notes, VaultConstraints.MAX_NOTES_CHARS, "Заметки")
            validateLength(entry.updatedUtc, VaultConstraints.MAX_UPDATED_UTC_CHARS, "Дата изменения")
            validateLength(entry.passwordUpdatedUtc, VaultConstraints.MAX_UPDATED_UTC_CHARS, "Дата изменения пароля")
            entry.passwordHistoryJson?.let {
                validateLength(it, VaultConstraints.MAX_PASSWORD_HISTORY_JSON_CHARS, "История паролей")
                parsePreservedArray(it)
            }
            entry.unknownJson?.let {
                validateLength(it, VaultConstraints.MAX_UNKNOWN_ENTRY_JSON_CHARS, "Неизвестные свойства записи")
            }
            if (entry.customFields.size > VaultConstraints.MAX_CUSTOM_FIELDS_PER_ENTRY) {
                throw VaultException("В одной записи слишком много дополнительных полей.")
            }
            entry.customFields.forEach { field ->
                validateLength(field.name, VaultConstraints.MAX_FIELD_NAME_CHARS, "Название дополнительного поля")
                validateLength(field.value, VaultConstraints.MAX_FIELD_VALUE_CHARS, "Значение дополнительного поля")
                field.unknownJson?.let {
                    validateLength(it, VaultConstraints.MAX_UNKNOWN_FIELD_JSON_CHARS, "Неизвестные свойства дополнительного поля")
                }
            }
        }
    }

    private fun extractUnknownJson(
        source: JSONObject,
        knownKeys: Set<String>,
        maxChars: Int,
        label: String,
    ): String? {
        val unknown = JSONObject()
        val keys = source.keys()
        var count = 0
        while (keys.hasNext()) {
            val key = keys.next()
            if (key in knownKeys) continue
            count++
            if (count > VaultConstraints.MAX_UNKNOWN_PROPERTIES_PER_OBJECT) {
                throw VaultException("$label: слишком много свойств.")
            }
            unknown.put(key, source.opt(key))
        }
        if (count == 0) return null
        return unknown.toString().also { validateLength(it, maxChars, label) }
    }

    private fun parsePreservedObject(json: String?): JSONObject =
        if (json.isNullOrBlank()) JSONObject() else try {
            JSONObject(json)
        } catch (error: Exception) {
            throw VaultException("Неизвестные свойства записи повреждены.", error)
        }

    private fun parsePreservedArray(json: String): JSONArray = try {
        JSONArray(json)
    } catch (error: Exception) {
        throw VaultException("История паролей повреждена.", error)
    }

    private fun readString(objectValue: JSONObject, name: String, maxChars: Int): String {
        val raw = objectValue.opt(name)
        val value = when (raw) {
            null, JSONObject.NULL -> ""
            is String -> raw
            else -> throw VaultException("Поле $name имеет неверный тип.")
        }
        validateLength(value, maxChars, "Поле $name")
        return value
    }

    private fun readBoolean(objectValue: JSONObject, name: String): Boolean {
        val raw = objectValue.opt(name)
        return when (raw) {
            null, JSONObject.NULL -> false
            is Boolean -> raw
            else -> throw VaultException("Поле $name имеет неверный тип.")
        }
    }

    private fun normalizeLineEndings(value: String): String =
        value.replace("\r\n", "\n").replace('\r', '\n')

    private fun validateLength(value: String, maxChars: Int, label: String) {
        if (value.length > maxChars) throw VaultException("$label превышает допустимую длину.")
    }
}
