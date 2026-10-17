package com.bedside.remote

import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Typography
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.compositionLocalOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.Font
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.sp

val LocalReducedMotion = compositionLocalOf { false }
val LocalHapticsEnabled = compositionLocalOf { false }
private val BodyFont = FontFamily(Font(R.font.dm_sans))
private val DisplayFont = FontFamily(Font(R.font.manrope, FontWeight.Bold))
private val Night =
    darkColorScheme(
        primary = Color(0xFFD8B57A),
        onPrimary = Color(0xFF272014),
        primaryContainer = Color(0xFF403420),
        onPrimaryContainer = Color(0xFFF1DBB6),
        secondary = Color(0xFFAAA59C),
        onSecondary = Color(0xFF171819),
        secondaryContainer = Color(0xFF303236),
        onSecondaryContainer = Color(0xFFF3EEE6),
        background = Color(0xFF101113),
        onBackground = Color(0xFFF3EEE6),
        surface = Color(0xFF181A1D),
        onSurface = Color(0xFFF3EEE6),
        surfaceVariant = Color(0xFF24272B),
        onSurfaceVariant = Color(0xFFC0BDB7),
        outline = Color(0xFF6E706F),
        error = Color(0xFFFFB4AB),
    )
private val Day =
    lightColorScheme(
        primary = Color(0xFF79571F),
        onPrimary = Color.White,
        primaryContainer = Color(0xFFEFDCBA),
        onPrimaryContainer = Color(0xFF36270E),
        secondaryContainer = Color(0xFFE7E3DB),
        onSecondaryContainer = Color(0xFF202122),
        background = Color(0xFFF6F3ED),
        onBackground = Color(0xFF202122),
        surface = Color(0xFFFFFCF6),
        onSurface = Color(0xFF202122),
        surfaceVariant = Color(0xFFE7E3DB),
        onSurfaceVariant = Color(0xFF514D46),
        outline = Color(0xFF77736B),
    )

@Composable
fun BedsideTheme(light: Boolean = false, content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = if (light) Day else Night,
        typography =
            Typography(
                displaySmall =
                    TextStyle(fontFamily = DisplayFont, fontSize = 32.sp, lineHeight = 40.sp),
                headlineMedium =
                    TextStyle(fontFamily = DisplayFont, fontSize = 26.sp, lineHeight = 34.sp),
                titleLarge =
                    TextStyle(fontFamily = DisplayFont, fontSize = 21.sp, lineHeight = 28.sp),
                titleMedium =
                    TextStyle(
                        fontFamily = BodyFont,
                        fontWeight = FontWeight.Medium,
                        fontSize = 16.sp,
                        lineHeight = 24.sp,
                    ),
                bodyLarge = TextStyle(fontFamily = BodyFont, fontSize = 16.sp, lineHeight = 24.sp),
                bodyMedium = TextStyle(fontFamily = BodyFont, fontSize = 14.sp, lineHeight = 20.sp),
                bodySmall = TextStyle(fontFamily = BodyFont, fontSize = 12.sp, lineHeight = 18.sp),
                labelLarge =
                    TextStyle(
                        fontFamily = BodyFont,
                        fontWeight = FontWeight.Medium,
                        fontSize = 14.sp,
                        lineHeight = 20.sp,
                    ),
                labelMedium =
                    TextStyle(fontFamily = BodyFont, fontSize = 12.sp, lineHeight = 18.sp),
                labelSmall =
                    TextStyle(
                        fontFamily = BodyFont,
                        fontSize = 11.sp,
                        lineHeight = 16.sp,
                        letterSpacing = 1.sp,
                    ),
            ),
        content = content,
    )
}
