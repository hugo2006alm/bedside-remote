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
        compose.onNode(hasSetTextAction()).performTextInput("Keep café 😀")
        compose.onNodeWithText("Send text").performClick()
        compose.onNode(hasSetTextAction()).assertTextContains("Keep café 😀")
        compose.runOnIdle { state.value = state.value.copy(textPending = false, error = "Busy") }
        compose.onNode(hasSetTextAction()).assertTextContains("Keep café 😀")
        compose.runOnIdle { state.value = state.value.copy(textAccepted = 1) }
        compose
            .onNode(hasSetTextAction())
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
    fun disconnectedDpadCannotSendInput() {
        compose.setContent { BedsideTheme { DPad(false) {} } }
        compose.onNodeWithContentDescription("Move down").assertIsNotEnabled()
    }
}
