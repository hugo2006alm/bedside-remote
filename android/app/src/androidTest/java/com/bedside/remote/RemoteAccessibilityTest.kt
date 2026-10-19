package com.bedside.remote

import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.test.*
import androidx.compose.ui.test.assertHasClickAction
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.performClick
import org.junit.Assert.assertEquals
import org.junit.Rule
import org.junit.Test

class RemoteAccessibilityTest {
    @get:Rule val compose = createComposeRule()

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
        compose.onNodeWithText("Choose browser tab").performClick()
        compose.onNodeWithText("Remaining paused tab").performClick()
        assertEquals("remaining:2", selected)
    }

    @Test
    fun fingerprintConfirmationHasOneNamedCheckboxTarget() {
        compose.setContent {
            BedsideTheme { PairDialog(false, null, null, { _, _, _, _, _ -> }, {}) }
        }
        compose
            .onNodeWithText("I compared all 64 fingerprint digits with Windows")
            .performScrollTo()
            .assertHasClickAction()
            .performClick()
            .assertIsOn()
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
