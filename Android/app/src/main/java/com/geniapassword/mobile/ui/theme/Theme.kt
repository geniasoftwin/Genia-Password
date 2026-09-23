package com.geniapassword.mobile.ui.theme

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val GeniaColorScheme = lightColorScheme(
    primary = GeniaPrimary,
    onPrimary = Color.White,
    primaryContainer = GeniaPrimarySoft,
    onPrimaryContainer = GeniaText,
    secondary = GeniaSecondary,
    background = GeniaBackground,
    onBackground = GeniaText,
    surface = GeniaSurface,
    onSurface = GeniaText,
    surfaceVariant = GeniaSurfaceVariant,
    onSurfaceVariant = GeniaTextMuted,
    outline = GeniaOutline,
    error = GeniaRed,
    onError = Color.White,
    errorContainer = GeniaRedSoft,
)

@Composable
fun GeniaPasswordTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = GeniaColorScheme,
        typography = Typography,
        content = content,
    )
}
