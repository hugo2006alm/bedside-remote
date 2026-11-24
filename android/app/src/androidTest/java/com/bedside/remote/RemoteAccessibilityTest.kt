package com.bedside.remote

import android.graphics.Bitmap
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.*
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.performClick
import androidx.test.platform.app.InstrumentationRegistry
import java.io.File
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class RemoteAccessibilityTest {
    @get:Rule val compose = createComposeRule()

    @Test
    fun multipleAvailableComputersRequireAnExplicitChoice() {
        val first = PairedComputer("first", "Office PC", "192.168.1.2", 8765, "", "")
        val second = PairedComputer("second", "Bedroom PC", "192.168.1.3", 8765, "", "")
        var selected: String? = null
        compose.setContent {
            BedsideTheme {
                RemoteContent(
                    AppState(
                        computers = listOf(first, second),
                        reachableIds = setOf("first", "second"),
                    ),
                    RemoteActions(select = { selected = it.deviceId }),
                )
            }
        }
        compose.onNodeWithText("Choose which computer to control.").assertIsDisplayed()
        assertEquals(null, selected)
        compose.onAllNodesWithText("Connect")[1].performScrollTo().performClick()
        assertEquals("second", selected)
    }

    @Test
    fun remoteOpensOnTrackpadWithNoPermanentDirectionalPad() {
        compose.setContent {
            BedsideTheme {
                RemoteContent(AppState(status = ConnectionStatus.CONNECTED), RemoteActions())
            }
        }
        compose
            .onNodeWithContentDescription("Computer trackpad", substring = true)
            .assertIsDisplayed()
        compose.onNodeWithContentDescription("Open keyboard").assertIsDisplayed()
        compose.onNodeWithContentDescription("Move up").assertDoesNotExist()
        compose.onNodeWithText("Touch").assertDoesNotExist()
    }

    @Test
    fun captureRemoteWithAndWithoutContextualPlayback() {
        val current = mutableStateOf(AppState())
        val light = mutableStateOf(false)
        compose.setContent {
            BedsideTheme(light.value) { RemoteContent(current.value, RemoteActions()) }
        }
        val connected = AppState(status = ConnectionStatus.CONNECTED)
        val youtube =
            Playback(
                platform = "youtube",
                tabId = 1,
                title = "A quiet evening",
                paused = false,
                position = 318.0,
                duration = 1200.0,
                ended = false,
                capabilities = listOf("playPause", "seek", "fullscreen"),
            )
        val netflix = youtube.copy(platform = "netflix", title = "Evening series · Episode 3")
        val netflixActions =
            netflix.copy(
                capabilities = netflix.capabilities + listOf("skipIntro", "nextEpisode", "audio")
            )
        val scenarios =
            listOf(
                Triple("disconnected", AppState(), null),
                Triple("idle", connected, null),
                Triple("youtube", connected, youtube),
                Triple("netflix", connected, netflix),
                Triple("netflix-actions", connected, netflixActions),
            )
        val output = InstrumentationRegistry.getArguments().getString("additionalTestOutputDir")
        for (lightMode in listOf(false, true)) {
            for ((name, base, playback) in scenarios) {
                compose.runOnIdle {
                    light.value = lightMode
                    current.value =
                        base.copy(
                            remote =
                                RemoteState(
                                    extensionConnected = playback != null,
                                    playback = playback,
                                )
                        )
                }
                compose
                    .onNodeWithContentDescription("Computer trackpad", substring = true)
                    .assertIsDisplayed()
                if (playback != null) {
                    compose.onNodeWithContentDescription("Pause browser").assertIsDisplayed()
                    compose.onNodeWithContentDescription("Toggle fullscreen").assertIsDisplayed()
                }
                if (playback == netflixActions)
                    compose.onNodeWithText("Skip intro").assertIsDisplayed()
                if (output != null) {
                    val theme = if (lightMode) "light" else "dark"
                    val directory = File(output).apply { mkdirs() }
                    val screenshot = compose.onRoot().captureToImage().asAndroidBitmap()
                    File(directory, "remote-$theme-$name.png").outputStream().use {
                        screenshot.compress(Bitmap.CompressFormat.PNG, 100, it)
                    }
                }
            }
        }
    }

    @Test
    fun keyboardKeepsRejectedDraftAndClearsOnlyAfterAcceptedAcknowledgement() {
        val state = mutableStateOf(AppState(status = ConnectionStatus.CONNECTED))
        compose.setContent {
            BedsideTheme {
                KeyboardDialog(
                    state.value,
                    RemoteActions(text = { state.value = state.value.copy(textPending = true) }),
                    {},
                )
            }
        }
        compose.onNodeWithText("Text to send").performTextInput("Keep café 😀")
        compose.onNodeWithText("Send text").performClick()
        compose.onNodeWithText("Text to send").assertTextContains("Keep café 😀")
        compose.runOnIdle { state.value = state.value.copy(textPending = false, error = "Busy") }
        compose.onNodeWithText("Text to send").assertTextContains("Keep café 😀")
        compose.runOnIdle { state.value = state.value.copy(textAccepted = 1) }
        compose
            .onNodeWithText("Text to send")
            .assert(
                SemanticsMatcher.expectValue(
                    androidx.compose.ui.semantics.SemanticsProperties.EditableText,
                    androidx.compose.ui.text.AnnotatedString(""),
                )
            )
    }

    @Test
    fun survivingBrowserCanBeSelectedWhenPreviousTargetDisappears() {
        var selected = ""
        val state =
            AppState(
                status = ConnectionStatus.CONNECTED,
                remote =
                    RemoteState(
                        extensionConnected = true,
                        sessions =
                            listOf(
                                BrowserSession("remaining:2", 2, "youtube", "Remaining paused tab")
                            ),
                    ),
            )
        compose.setContent {
            BedsideTheme { PlaybackPanel(state, RemoteActions(session = { selected = it })) }
        }
        compose.onNodeWithContentDescription("Choose browser tab").performClick()
        compose.onNodeWithText("Remaining paused tab").performClick()
        assertEquals("remaining:2", selected)
    }

    @Test
    fun fingerprintConfirmationHasOneNamedCheckboxTarget() {
        compose.setContent {
            BedsideTheme {
                PairDialog(false, null, null, { _, _, _, _, _ -> }, {}, { _, _ -> "AB".repeat(32) })
            }
        }
        compose
            .onNodeWithText("I compared all 64 fingerprint digits with Windows")
            .performScrollTo()
            .assertIsNotEnabled()
        compose
            .onNodeWithText("Computer IP address")
            .performScrollTo()
            .performTextInput("127.0.0.1")
        compose.waitUntil(5000) {
            compose
                .onAllNodesWithText("ABABABAB ".repeat(7) + "ABABABAB")
                .fetchSemanticsNodes()
                .isNotEmpty()
        }
        compose
            .onNodeWithText("I compared all 64 fingerprint digits with Windows")
            .performScrollTo()
            .assertHasClickAction()
            .performClick()
            .assertIsOn()
    }

    @Test
    fun gridButtonOpensDetectedMediaAppsOnConnectedComputer() {
        var launched = ""
        compose.setContent {
            BedsideTheme {
                RemoteContent(
                    AppState(
                        status = ConnectionStatus.CONNECTED,
                        catalog =
                            AgentCatalog(
                                apps =
                                    listOf(
                                        MediaApp("youtube", "YouTube", "browser"),
                                        MediaApp("spotify", "Spotify", "app"),
                                    )
                            ),
                    ),
                    RemoteActions(launchApp = { launched = it }),
                )
            }
        }
        compose.onNodeWithContentDescription("Open media apps").performClick()
        compose.onNodeWithText("Open on your PC").assertExists()
        compose.onNodeWithText("Spotify").performClick()
        assertEquals("spotify", launched)
    }

    @Test
    fun netflixHasOneCombinedAudioAndSubtitlesControl() {
        var action = ""
        val playback =
            Playback(
                platform = "netflix",
                tabId = 1,
                title = "Series E03 Episode",
                paused = true,
                position = 0.0,
                duration = 0.0,
                ended = false,
                capabilities = listOf("audio", "subtitles"),
            )
        compose.setContent {
            BedsideTheme {
                PlaybackPanel(
                    AppState(
                        status = ConnectionStatus.CONNECTED,
                        remote = RemoteState(extensionConnected = true, playback = playback),
                    ),
                    RemoteActions(browser = { command, _ -> action = command }),
                )
            }
        }
        compose.onNodeWithText("Audio & subtitles").assertHasClickAction().performClick()
        assertEquals("audio", action)
        compose.onNodeWithText("Audio").assertDoesNotExist()
        compose.onNodeWithText("Subtitles").assertDoesNotExist()
    }

    @Test
    fun dpadExposesNamedTouchTargetsAndSendsCorrectKey() {
        var key = ""
        compose.setContent { BedsideTheme { DPad(true) { key = it } } }
        compose
            .onNodeWithContentDescription("Move up")
            .assertIsEnabled()
            .assertHasClickAction()
            .performClick()
        assertEquals("up", key)
        compose.onNodeWithContentDescription("OK / Enter").performClick()
        assertEquals("enter", key)
    }

    @Test
    fun browserDpadSelectsElementsAndWindowsModeRetainsUniversalKeys() {
        var navigation = ""
        var key = ""
        compose.setContent {
            BedsideTheme {
                RemoteContent(
                    AppState(
                        status = ConnectionStatus.CONNECTED,
                        remote = RemoteState(activeSessionId = "browser:1"),
                    ),
                    RemoteActions(key = { key = it }, navigate = { navigation = it }),
                )
            }
        }
        compose.onNodeWithContentDescription("Windows controls").performClick()
        compose.onNodeWithText("Browser").performScrollTo().performClick()
        compose.onNodeWithContentDescription("Move right").performScrollTo().performClick()
        assertEquals("right", navigation)
        assertEquals("", key)
        compose.onNodeWithText("Windows").performScrollTo().performClick()
        compose.onNodeWithContentDescription("Move down").performScrollTo().performClick()
        assertEquals("down", key)
        compose.onNodeWithContentDescription("OK / Enter").performClick()
        assertEquals("enter", key)
    }

    @Test
    fun disconnectedDpadCannotSendInput() {
        compose.setContent { BedsideTheme { DPad(false) {} } }
        compose.onNodeWithContentDescription("Move down").assertIsNotEnabled()
    }
}
