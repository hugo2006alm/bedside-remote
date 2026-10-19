package com.bedside.remote

import org.junit.Assert.*
import org.junit.Test

class TrackpadGestureTest {
    private val events = mutableListOf<TrackpadAction>()

    private fun gesture(drag: Boolean = true) = TrackpadGesture(8f, 300, 500, drag, events::add)

    @Test
    fun doubleTapHoldDragsAndReleasesWithoutAnExtraClick() {
        val pad = gesture()
        pad.down(0, 10f, 10f)
        pad.up(60)
        pad.down(160, 10f, 10f)
        pad.move(1, 25f, 5f)
        pad.up(700)
        assertEquals(
            listOf(
                TrackpadAction.Click(false),
                TrackpadAction.Drag("start"),
                TrackpadAction.Drag("move", 25f, 5f),
                TrackpadAction.Drag("end"),
            ),
            events,
        )
        assertFalse(pad.dragging)
    }

    @Test
    fun twoFingerTapClicksRightWithoutMovingOrScrolling() {
        val pad = gesture()
        pad.down(0, 10f, 10f)
        pad.move(2, 0f, 0f)
        pad.move(2, 0f, 1f)
        pad.move(1, 0f, 0f)
        pad.up(140)
        assertEquals(listOf(TrackpadAction.Click(true)), events)
    }

    @Test
    fun twoFingerScrollHasWindowsDirectionAndCannotClickOnRelease() {
        val pad = gesture()
        pad.down(0, 10f, 10f)
        pad.move(2, 0f, 12f)
        pad.move(1, 0f, 0f)
        pad.up(200)
        assertEquals(listOf(TrackpadAction.Scroll(48)), events)
    }

    @Test
    fun cancellationAndSwitchingToTwoFingersReleaseHeldButton() {
        val pad = gesture()
        pad.down(0, 10f, 10f)
        pad.up(50)
        pad.down(100, 10f, 10f)
        pad.move(2, 0f, 12f)
        pad.cancel()
        assertEquals(1, events.count { it == TrackpadAction.Drag("end") })
        assertFalse(pad.dragging)
    }

    @Test
    fun olderAgentNeverReceivesUnsupportedDragCommands() {
        val pad = gesture(false)
        pad.down(0, 10f, 10f)
        pad.up(50)
        pad.down(100, 10f, 10f)
        pad.move(1, 20f, 0f)
        pad.up(200)
        assertTrue(events.none { it is TrackpadAction.Drag })
        assertEquals(TrackpadAction.Move(20f, 0f), events.last())
    }

    @Test
    fun distantOrLateSecondTapDoesNotBeginDrag() {
        val pad = gesture()
        pad.down(0, 10f, 10f)
        pad.up(50)
        pad.down(100, 200f, 200f)
        pad.up(150)
        pad.down(900, 200f, 200f)
        pad.up(950)
        assertTrue(events.all { it == TrackpadAction.Click(false) })
    }
}
