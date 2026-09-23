package com.geniapassword.mobile.security

import java.security.SecureRandom

object PasswordGenerator {
    private val random = SecureRandom()

    private const val LOWER = "abcdefghijklmnopqrstuvwxyz"
    private const val UPPER = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
    private const val DIGITS = "0123456789"
    private const val SYMBOLS = "!@#$%^&*()-_=+[]{}|;:,.<>?"
    private const val AMBIGUOUS = "O0oIl1|`'\""

    fun generate(
        length: Int = 16,
        useUpper: Boolean = true,
        useDigits: Boolean = true,
        useSymbols: Boolean = true,
        excludeAmbiguous: Boolean = false,
    ): String {
        require(length in 8..128) { "Длина пароля должна быть от 8 до 128 символов." }

        fun pool(value: String): String =
            if (!excludeAmbiguous) value else value.filterNot { it in AMBIGUOUS }

        val requiredPools = buildList {
            add(pool(LOWER))
            if (useUpper) add(pool(UPPER))
            if (useDigits) add(pool(DIGITS))
            if (useSymbols) add(pool(SYMBOLS))
        }.filter { it.isNotEmpty() }

        require(requiredPools.isNotEmpty()) { "Выберите хотя бы одну группу символов." }
        require(length >= requiredPools.size) { "Длина пароля слишком мала для выбранных групп символов." }

        val fullPool = requiredPools.joinToString(separator = "")
        val chars = CharArray(length)
        var index = 0

        requiredPools.forEach { currentPool ->
            chars[index++] = currentPool[random.nextInt(currentPool.length)]
        }
        while (index < chars.size) {
            chars[index++] = fullPool[random.nextInt(fullPool.length)]
        }

        // Fisher-Yates с SecureRandom: обязательные символы не остаются в начале.
        for (i in chars.lastIndex downTo 1) {
            val j = random.nextInt(i + 1)
            val tmp = chars[i]
            chars[i] = chars[j]
            chars[j] = tmp
        }
        return String(chars)
    }
}
