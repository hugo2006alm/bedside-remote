package com.bedside.remote

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
