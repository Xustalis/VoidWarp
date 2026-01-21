package com.voidwarp.android.ui.theme

import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

private val DarkColorScheme = darkColorScheme(
    primary = Color(0xFF6C63FF),
    onPrimary = Color.White,
    secondary = Color(0xFF4A4E69),
    onSecondary = Color.White,
    background = Color(0xFF1A1A2E),
    onBackground = Color.White,
    surface = Color(0xFF16213E),
    onSurface = Color.White
)

@Composable
fun VoidWarpTheme(
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = DarkColorScheme,
        content = content
    )
}
