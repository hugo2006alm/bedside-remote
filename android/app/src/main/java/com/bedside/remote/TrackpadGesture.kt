package com.bedside.remote

import kotlin.math.abs

internal sealed interface TrackpadAction {
    data class Move(val dx: Float, val dy: Float) : TrackpadAction

    data class Scroll(val delta: Int) : TrackpadAction

    data class Click(val right: Boolean) : TrackpadAction

    data class Drag(val phase: String, val dx: Float = 0f, val dy: Float = 0f) : TrackpadAction
}

internal class TrackpadGesture(
    private val slop: Float,
    private val doubleTapMillis: Long,
    private val tapMillis: Long,
    private val dragSupported: Boolean,
    private val emit: (TrackpadAction) -> Unit,
) {
    var dragging = false
        private set

    private var lastTap: Long? = null
    private var tapX = 0f
    private var tapY = 0f
    private var started = 0L
    private var startX = 0f
    private var startY = 0f
    private var distance = 0f
    private var twoFingers = false
    private var remainder = 0f

    fun down(time: Long, x: Float, y: Float) {
        started = time
        startX = x
        startY = y
        distance = 0f
        twoFingers = false
        remainder = 0f
        dragging =
            dragSupported &&
                lastTap?.let { time - it in 0..doubleTapMillis } == true &&
                abs(x - tapX) + abs(y - tapY) < slop * 4
        lastTap = null
        if (dragging) emit(TrackpadAction.Drag("start"))
    }

    fun move(fingers: Int, dx: Float, dy: Float) {
        distance += abs(dx) + abs(dy)
        if (fingers > 1) {
            twoFingers = true
            if (dragging) {
                dragging = false
                emit(TrackpadAction.Drag("end"))
            }
            // Windows-style direction: dragging fingers down sends wheel-up.
            if (distance >= slop) remainder += dy * 4
            val steps = remainder.toInt()
            if (steps != 0) {
                emit(TrackpadAction.Scroll(steps))
                remainder -= steps
            }
        } else if (!twoFingers && fingers == 1 && (dx != 0f || dy != 0f)) {
            if (dragging) emit(TrackpadAction.Drag("move", dx, dy))
            else emit(TrackpadAction.Move(dx, dy))
        }
    }

    fun up(time: Long) {
        if (dragging) {
            dragging = false
            emit(TrackpadAction.Drag("end"))
            return
        }
        if (distance < slop && time - started <= tapMillis) {
            emit(TrackpadAction.Click(twoFingers))
            if (!twoFingers) {
                lastTap = time
                tapX = startX
                tapY = startY
            }
        }
    }

    fun cancel() {
        if (dragging) emit(TrackpadAction.Drag("end"))
        dragging = false
        lastTap = null
    }
}
