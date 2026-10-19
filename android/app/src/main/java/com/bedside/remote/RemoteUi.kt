package com.bedside.remote

import android.content.Intent
import android.net.Uri
import androidx.compose.animation.Crossfade
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.animation.core.snap
import androidx.compose.animation.core.spring
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.gestures.awaitEachGesture
import androidx.compose.foundation.gestures.awaitFirstDown
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.interaction.collectIsPressedAsState
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.toggleable
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.rounded.*
import androidx.compose.material.icons.rounded.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.input.pointer.positionChange
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.semantics.*
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.tooling.preview.Preview
import androidx.compose.ui.unit.dp
import coil.compose.AsyncImage
import java.net.URI
import kotlin.math.abs

data class RemoteActions(
    val key: (String) -> Unit = {},
    val navigate: (String) -> Unit = key,
    val media: (String) -> Unit = {},
    val browser: (String, Double?) -> Unit = { _, _ -> },
    val session: (String) -> Unit = {},
    val text: (String) -> Unit = {},
    val pointer: (Float, Float) -> Unit = { _, _ -> },
    val click: (Boolean) -> Unit = {},
    val scroll: (Int) -> Unit = {},
    val enqueue: (String, String) -> Unit = { _, _ -> },
    val queue: (String) -> Unit = {},
    val remove: (String) -> Unit = {},
    val move: (String, Int) -> Unit = { _, _ -> },
    val select: (PairedComputer) -> Unit = {},
    val forget: (PairedComputer) -> Unit = {},
    val updateEndpoint: (PairedComputer, String, String) -> Unit = { _, _, _ -> },
    val pair: (String, String, String, String, Boolean) -> Unit = { _, _, _, _, _ -> },
    val cancelPairing: () -> Unit = {},
    val haptics: (Boolean) -> Unit = {},
    val reducedMotion: (Boolean) -> Unit = {},
    val lightTheme: (Boolean) -> Unit = {},
    val sensitivity: (Float) -> Unit = {},
    val dismissShare: () -> Unit = {},
)

@Composable
fun BedsideApp(state: AppState, model: RemoteViewModel) {
    val actions =
        remember(model) {
            RemoteActions(
                key = model::key,
                navigate = model::navigate,
                media = model::media,
                browser = model::browser,
                session = model::session,
                text = model::text,
                pointer = model::pointer,
                click = model::click,
                scroll = model::scroll,
                enqueue = model::enqueue,
                queue = model::queue,
                remove = model::remove,
                move = model::move,
                select = model::select,
                forget = {
                    model.forget(it)
                    Unit
                },
                updateEndpoint = model::updateEndpoint,
                pair = model::pair,
                cancelPairing = model::cancelPairing,
                haptics = model::haptics,
                reducedMotion = model::reducedMotion,
                lightTheme = model::lightTheme,
                sensitivity = model::sensitivity,
                dismissShare = model::dismissShare,
            )
        }
    BedsideTheme(state.lightTheme) {
        CompositionLocalProvider(
            LocalReducedMotion provides state.reducedMotion,
            LocalHapticsEnabled provides state.haptics,
        ) {
            val snackbar = remember { SnackbarHostState() }
            LaunchedEffect(state.error) {
                state.error?.let {
                    snackbar.showSnackbar(
                        it,
                        withDismissAction = true,
                        duration = SnackbarDuration.Long,
                    )
                    model.dismissError()
                }
            }
            LaunchedEffect(state.notice) {
                state.notice?.let {
                    snackbar.showSnackbar(it, withDismissAction = true)
                    model.dismissNotice()
                }
            }
            RemoteContent(state, actions, snackbar)
        }
    }
}

private enum class Screen(val label: String, val icon: ImageVector) {
    REMOTE("Remote", Icons.Rounded.SettingsRemote),
    TOUCH("Touch", Icons.Rounded.TouchApp),
    QUEUE("Queue", Icons.AutoMirrored.Rounded.QueueMusic),
    DEVICES("Devices", Icons.Rounded.DesktopWindows),
}

@Composable
fun RemoteContent(
    state: AppState,
    actions: RemoteActions,
    snackbar: SnackbarHostState = remember { SnackbarHostState() },
) {
    var screen by rememberSaveable { mutableStateOf(Screen.REMOTE) }
    var keyboard by rememberSaveable { mutableStateOf(false) }
    var settings by rememberSaveable { mutableStateOf(false) }
    var shareVisible by remember(state.sharedUrl) { mutableStateOf(state.sharedUrl != null) }
    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        snackbarHost = { SnackbarHost(snackbar) },
        topBar = {
            Row(
                Modifier.statusBarsPadding()
                    .fillMaxWidth()
                    .padding(start = 24.dp, end = 12.dp, top = 12.dp, bottom = 8.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Image(
                    painterResource(R.drawable.ic_brand_foreground),
                    contentDescription = null,
                    modifier =
                        Modifier.padding(end = 12.dp)
                            .size(40.dp)
                            .background(
                                androidx.compose.ui.graphics.Color(0xFF101113),
                                RoundedCornerShape(12.dp),
                            ),
                )
                Column(Modifier.weight(1f)) {
                    Text(
                        "BEDSIDE / REMOTE",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.primary,
                    )
                    Text(
                        state.selected?.name ?: "Your space, connected.",
                        style = MaterialTheme.typography.titleMedium,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                IconButton(onClick = { screen = Screen.DEVICES }) {
                    Icon(
                        Icons.Rounded.FiberManualRecord,
                        "Connection: ${statusLabel(state.status)}",
                        tint =
                            if (state.connected) MaterialTheme.colorScheme.primary
                            else MaterialTheme.colorScheme.outline,
                        modifier = Modifier.size(12.dp),
                    )
                }
                IconButton(onClick = { settings = true }) { Icon(Icons.Rounded.Tune, "Settings") }
            }
        },
        bottomBar = {
            NavigationBar(
                Modifier.navigationBarsPadding()
                    .padding(horizontal = 16.dp, vertical = 8.dp)
                    .clip(RoundedCornerShape(28.dp)),
                containerColor = MaterialTheme.colorScheme.surface,
                tonalElevation = 0.dp,
            ) {
                Screen.entries.forEach { item ->
                    NavigationBarItem(
                        selected = screen == item,
                        onClick = { screen = item },
                        icon = { Icon(item.icon, item.label) },
                        label = { Text(item.label) },
                        colors =
                            NavigationBarItemDefaults.colors(
                                indicatorColor = MaterialTheme.colorScheme.surfaceVariant
                            ),
                    )
                }
            }
        },
    ) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)) {
            if (!state.connected) {
                Surface(
                    onClick = { screen = Screen.DEVICES },
                    color = MaterialTheme.colorScheme.surfaceVariant,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = 24.dp, vertical = 6.dp),
                    shape = RoundedCornerShape(16.dp),
                ) {
                    Row(Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
                        if (
                            state.status == ConnectionStatus.CONNECTING ||
                                state.status == ConnectionStatus.RECONNECTING
                        ) {
                            CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                            Spacer(Modifier.width(12.dp))
                        }
                        Text(
                            if (state.computers.isEmpty()) "Pair your computer to begin"
                            else statusLabel(state.status),
                            style = MaterialTheme.typography.bodyMedium,
                            modifier = Modifier.weight(1f),
                        )
                        Icon(
                            Icons.AutoMirrored.Rounded.ArrowForward,
                            "Open devices",
                            Modifier.size(18.dp),
                        )
                    }
                }
            }
            if (state.sharedUrl != null && !shareVisible)
                TextButton(onClick = { shareVisible = true }, modifier = Modifier.fillMaxWidth()) {
                    Text("Shared YouTube video · choose action")
                }
            Crossfade(
                targetState = screen,
                animationSpec = if (LocalReducedMotion.current) snap() else spring(),
                label = "Control surface",
            ) { active ->
                when (active) {
                    Screen.REMOTE -> RemoteScreen(state, actions, onKeyboard = { keyboard = true })
                    Screen.TOUCH -> TouchScreen(state, actions, onKeyboard = { keyboard = true })
                    Screen.QUEUE -> QueueScreen(state, actions)
                    Screen.DEVICES -> DevicesScreen(state, actions)
                }
            }
        }
    }
    if (keyboard) KeyboardDialog(state, actions, onDismiss = { keyboard = false })
    if (settings) SettingsDialog(state, actions, onDismiss = { settings = false })
    if (state.sharedUrl != null && shareVisible) {
        VideoDialog(
            state.sharedUrl,
            state.connected,
            actions.enqueue,
            pending = state.videoPending,
            accepted = state.videoAccepted,
            acceptedUrl = state.videoAcceptedUrl,
            error = state.error,
            onDismiss = { actions.dismissShare() },
            onDevices = {
                shareVisible = false
                screen = Screen.DEVICES
            },
        )
    }
}

private fun statusLabel(status: ConnectionStatus) =
    when (status) {
        ConnectionStatus.CONNECTED -> "Connected securely"
        ConnectionStatus.CONNECTING -> "Connecting securely…"
        ConnectionStatus.RECONNECTING -> "Reconnecting… Keep your computer awake"
        ConnectionStatus.REVOKED -> "Authorization revoked · pair again"
        ConnectionStatus.DISCONNECTED -> "Disconnected · choose a computer"
    }

@Composable
private fun RemoteScreen(state: AppState, actions: RemoteActions, onKeyboard: () -> Unit) {
    var browserNavigation by rememberSaveable { mutableStateOf(true) }
    val browserTarget = browserNavigation && state.remote.activeSessionId != null
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 24.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(18.dp),
    ) {
        Row(Modifier.fillMaxWidth().padding(top = 12.dp), verticalAlignment = Alignment.Bottom) {
            Text(
                "Settle in.",
                style = MaterialTheme.typography.displaySmall,
                modifier = Modifier.weight(1f),
            )
            Text(
                if (browserTarget) "BROWSER" else "UNIVERSAL",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(bottom = 6.dp),
            )
        }
        if (state.remote.activeSessionId != null) {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                FilterChip(
                    selected = !browserNavigation,
                    onClick = { browserNavigation = false },
                    label = { Text("Windows") },
                )
                FilterChip(
                    selected = browserNavigation,
                    onClick = { browserNavigation = true },
                    label = {
                        Text(
                            if (state.remote.playback?.platform == "netflix") "Netflix"
                            else "Browser"
                        )
                    },
                )
            }
        }
        DPad(state.connected, if (browserTarget) actions.navigate else actions.key)
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
            Control(Icons.AutoMirrored.Rounded.ArrowBack, "Back / Escape", state.connected) {
                actions.key("escape")
            }
            Control(Icons.Rounded.Window, "Windows Start menu", state.connected) {
                actions.key("home")
            }
            Control(Icons.Rounded.Keyboard, "Open keyboard", state.connected, onClick = onKeyboard)
        }
        Surface(
            shape = RoundedCornerShape(28.dp),
            color = MaterialTheme.colorScheme.surface,
            modifier = Modifier.fillMaxWidth(),
        ) {
            Column(
                Modifier.padding(vertical = 10.dp),
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceEvenly,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Control(Icons.Rounded.SkipPrevious, "Previous media", state.connected) {
                        actions.media("previous")
                    }
                    Control(Icons.Rounded.Replay10, "Seek backward", state.connected) {
                        actions.media("seekBackward")
                    }
                    Control(
                        Icons.Rounded.PlayArrow,
                        "Play or pause media",
                        state.connected,
                        prominent = true,
                    ) {
                        actions.media("playPause")
                    }
                    Control(Icons.Rounded.Forward10, "Seek forward", state.connected) {
                        actions.media("seekForward")
                    }
                    Control(Icons.Rounded.SkipNext, "Next media", state.connected) {
                        actions.media("next")
                    }
                }
                HorizontalDivider(
                    Modifier.padding(horizontal = 20.dp, vertical = 6.dp),
                    color = MaterialTheme.colorScheme.outline.copy(alpha = .2f),
                )
                Row(
                    Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceEvenly,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Control(
                        Icons.AutoMirrored.Rounded.VolumeDown,
                        "Lower volume",
                        state.connected,
                    ) {
                        actions.media("volumeDown")
                    }
                    Control(
                        Icons.AutoMirrored.Rounded.VolumeOff,
                        "Mute or unmute",
                        state.connected,
                    ) {
                        actions.media("mute")
                    }
                    Control(Icons.AutoMirrored.Rounded.VolumeUp, "Raise volume", state.connected) {
                        actions.media("volumeUp")
                    }
                }
            }
        }
        PlaybackPanel(state, actions)
        Spacer(Modifier.height(8.dp))
    }
}

@Composable
@Suppress("DEPRECATION") // D-pad arrows represent absolute Windows directions, including in RTL.
fun DPad(enabled: Boolean, onKey: (String) -> Unit) {
    Box(
        Modifier.size(272.dp)
            .background(MaterialTheme.colorScheme.surface, CircleShape)
            .padding(8.dp),
        contentAlignment = Alignment.Center,
    ) {
        Surface(
            Modifier.size(128.dp),
            shape = CircleShape,
            color = MaterialTheme.colorScheme.background,
            border = BorderStroke(1.dp, MaterialTheme.colorScheme.outline.copy(alpha = .18f)),
        ) {}
        Box(Modifier.align(Alignment.TopCenter)) {
            Control(Icons.Rounded.KeyboardArrowUp, "Move up", enabled, size = 64) { onKey("up") }
        }
        Box(Modifier.align(Alignment.BottomCenter)) {
            Control(Icons.Rounded.KeyboardArrowDown, "Move down", enabled, size = 64) {
                onKey("down")
            }
        }
        Box(Modifier.align(Alignment.CenterStart)) {
            Control(Icons.Rounded.KeyboardArrowLeft, "Move left", enabled, size = 64) {
                onKey("left")
            }
        }
        Box(Modifier.align(Alignment.CenterEnd)) {
            Control(Icons.Rounded.KeyboardArrowRight, "Move right", enabled, size = 64) {
                onKey("right")
            }
        }
        val haptic = LocalHapticFeedback.current
        val haptics = LocalHapticsEnabled.current
        FilledTonalButton(
            onClick = {
                if (haptics) haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                onKey("enter")
            },
            enabled = enabled,
            modifier = Modifier.size(80.dp).semantics { contentDescription = "OK / Enter" },
            shape = CircleShape,
            contentPadding = PaddingValues(0.dp),
            colors =
                ButtonDefaults.filledTonalButtonColors(
                    containerColor = MaterialTheme.colorScheme.primary,
                    contentColor = MaterialTheme.colorScheme.onPrimary,
                ),
        ) {
            Text("OK", style = MaterialTheme.typography.titleMedium)
        }
    }
}

@Composable
private fun Control(
    icon: ImageVector,
    label: String,
    enabled: Boolean,
    prominent: Boolean = false,
    size: Int = 48,
    onClick: () -> Unit,
) {
    val source = remember { MutableInteractionSource() }
    val pressed by source.collectIsPressedAsState()
    val scale by
        animateFloatAsState(
            if (pressed) .93f else 1f,
            animationSpec = if (LocalReducedMotion.current) snap() else spring(),
            label = "Button press",
        )
    val haptic = LocalHapticFeedback.current
    val haptics = LocalHapticsEnabled.current
    IconButton(
        onClick = {
            if (haptics) haptic.performHapticFeedback(HapticFeedbackType.LongPress)
            onClick()
        },
        enabled = enabled,
        modifier =
            Modifier.size(size.dp)
                .scale(scale)
                .then(
                    if (prominent)
                        Modifier.background(
                            MaterialTheme.colorScheme.primary.copy(
                                alpha = if (enabled) 1f else .2f
                            ),
                            CircleShape,
                        )
                    else Modifier
                ),
        interactionSource = source,
    ) {
        Icon(
            icon,
            label,
            Modifier.size(if (prominent) 30.dp else 26.dp),
            tint =
                if (enabled) {
                    if (prominent) MaterialTheme.colorScheme.onPrimary
                    else MaterialTheme.colorScheme.onSurface
                } else MaterialTheme.colorScheme.onSurface.copy(alpha = .38f),
        )
    }
}

@Composable
internal fun PlaybackPanel(state: AppState, actions: RemoteActions) {
    val playback = state.remote.playback
    Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Row(
            Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                "NOW PLAYING",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (playback != null)
                Text(
                    playback.platform.uppercase(),
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.primary,
                )
        }
        if (
            state.remote.sessions.isNotEmpty() &&
                (state.remote.sessions.size > 1 || state.remote.activeSessionId == null)
        ) {
            var expanded by remember { mutableStateOf(false) }
            Box {
                TextButton(onClick = { expanded = true }) {
                    Icon(Icons.Rounded.Tab, null)
                    Spacer(Modifier.width(8.dp))
                    Text("Choose browser tab")
                }
                DropdownMenu(expanded, onDismissRequest = { expanded = false }) {
                    state.remote.sessions.forEach { session ->
                        DropdownMenuItem(
                            text = {
                                Text(
                                    "${if (session.sessionId == state.remote.activeSessionId) "● " else ""}${session.title}",
                                    maxLines = 2,
                                )
                            },
                            onClick = {
                                actions.session(session.sessionId)
                                expanded = false
                            },
                        )
                    }
                }
            }
        }
        if (!state.remote.extensionConnected || playback == null) {
            Text(
                if (!state.connected) "Connect to see your browser playback."
                else if (!state.remote.extensionConnected)
                    "Universal remote ready. Connect a browser companion for smart controls."
                else "Open YouTube in Zen / Firefox, or Netflix in the Edge Store app companion.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            return
        }
        Surface(shape = RoundedCornerShape(24.dp), color = MaterialTheme.colorScheme.surface) {
            Column(
                Modifier.fillMaxWidth().padding(18.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(14.dp),
                ) {
                    val thumbnail =
                        playback.thumbnail?.takeIf {
                            runCatching {
                                    val uri = URI(it)
                                    uri.scheme == "https" &&
                                        uri.host in setOf("i.ytimg.com", "img.youtube.com") &&
                                        uri.rawUserInfo == null &&
                                        uri.port == -1
                                }
                                .getOrDefault(false)
                        }
                    if (thumbnail != null)
                        AsyncImage(
                            thumbnail,
                            null,
                            modifier = Modifier.size(72.dp, 52.dp).clip(RoundedCornerShape(10.dp)),
                        )
                    Column(Modifier.weight(1f)) {
                        Text(
                            playback.title.ifBlank { "Browser playback" },
                            style = MaterialTheme.typography.titleMedium,
                            maxLines = 2,
                            overflow = TextOverflow.Ellipsis,
                        )
                        playback.channel
                            ?.takeIf { it.isNotBlank() }
                            ?.let {
                                Text(
                                    it,
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                    }
                }
                if (playback.duration > 0) {
                    var seeking by
                        remember(playback.tabId, playback.videoId, playback.title) {
                            mutableStateOf<Float?>(null)
                        }
                    Slider(
                        value =
                            seeking
                                ?: playback.position
                                    .toFloat()
                                    .coerceIn(0f, playback.duration.toFloat()),
                        onValueChange = { seeking = it },
                        onValueChangeFinished = {
                            seeking?.let { actions.browser("seek", it.toDouble()) }
                            seeking = null
                        },
                        valueRange = 0f..playback.duration.toFloat(),
                        enabled = state.connected && "seek" in playback.capabilities,
                        modifier = Modifier.semantics { contentDescription = "Playback position" },
                    )
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                        Text(
                            timeLabel(seeking?.toDouble() ?: playback.position),
                            style = MaterialTheme.typography.bodySmall,
                        )
                        Text(
                            timeLabel(playback.duration),
                            style = MaterialTheme.typography.bodySmall,
                        )
                    }
                } else
                    Text("Live or duration unavailable", style = MaterialTheme.typography.bodySmall)
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                    if ("seek" in playback.capabilities)
                        Control(
                            Icons.Rounded.Replay10,
                            "Browser back 10 seconds",
                            state.connected,
                        ) {
                            actions.browser("seek", (playback.position - 10).coerceAtLeast(0.0))
                        }
                    if ("playPause" in playback.capabilities)
                        Control(
                            if (playback.paused) Icons.Rounded.PlayArrow else Icons.Rounded.Pause,
                            if (playback.paused) "Play browser" else "Pause browser",
                            state.connected,
                            prominent = true,
                        ) {
                            actions.browser("playPause", null)
                        }
                    if ("seek" in playback.capabilities)
                        Control(
                            Icons.Rounded.Forward10,
                            "Browser forward 10 seconds",
                            state.connected,
                        ) {
                            actions.browser(
                                "seek",
                                (playback.position + 10).coerceAtMost(
                                    if (playback.duration > 0) playback.duration
                                    else Double.MAX_VALUE
                                ),
                            )
                        }
                    if ("fullscreen" in playback.capabilities)
                        Control(Icons.Rounded.Fullscreen, "Toggle fullscreen", state.connected) {
                            actions.browser("fullscreen", null)
                        }
                }
                val contextualActions = buildList {
                    add("skipIntro" to "Skip intro")
                    add("skipRecap" to "Skip recap")
                    add("nextEpisode" to "Next episode")
                    if (playback.platform == "netflix") {
                        if (
                            "audio" in playback.capabilities || "subtitles" in playback.capabilities
                        )
                            add(
                                (if ("audio" in playback.capabilities) "audio" else "subtitles") to
                                    "Audio & subtitles"
                            )
                    } else {
                        add("subtitles" to "Subtitles")
                        add("audio" to "Audio")
                    }
                }
                contextualActions
                    .filter { it.first in playback.capabilities }
                    .forEach { (command, label) ->
                        OutlinedButton(
                            onClick = { actions.browser(command, null) },
                            enabled = state.connected,
                            modifier = Modifier.fillMaxWidth(),
                        ) {
                            Text(label)
                        }
                    }
                playback.error?.let {
                    Text(
                        it,
                        color = MaterialTheme.colorScheme.error,
                        style = MaterialTheme.typography.bodyMedium,
                    )
                }
                if (playback.platform == "youtube") {
                    val next =
                        state.remote.queue.indexOfFirst {
                            it.entryId == state.remote.currentEntryId
                        } + 1
                    state.remote.queue.getOrNull(next)?.let {
                        Text(
                            "Up next · ${it.title}",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                            maxLines = 2,
                        )
                    }
                }
            }
        }
    }
}

private fun timeLabel(seconds: Double): String {
    val total = seconds.toLong().coerceAtLeast(0)
    return if (total >= 3600) "%d:%02d:%02d".format(total / 3600, total / 60 % 60, total % 60)
    else "%d:%02d".format(total / 60, total % 60)
}

@Composable
private fun TouchScreen(state: AppState, actions: RemoteActions, onKeyboard: () -> Unit) {
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(20.dp),
    ) {
        Text("A lighter touch.", style = MaterialTheme.typography.headlineMedium)
        Text(
            "Slide to move · tap to click\nTwo fingers to scroll · hold for right click",
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Box(
            Modifier.fillMaxWidth()
                .height(320.dp)
                .clip(RoundedCornerShape(32.dp))
                .background(MaterialTheme.colorScheme.surface)
                .semantics {
                    contentDescription =
                        "Computer trackpad. Slide one finger to move; use two fingers to scroll."
                    customActions =
                        listOf(
                            CustomAccessibilityAction("Left click") {
                                if (state.connected) {
                                    actions.click(false)
                                    true
                                } else false
                            },
                            CustomAccessibilityAction("Right click") {
                                if (state.connected) {
                                    actions.click(true)
                                    true
                                } else false
                            },
                            CustomAccessibilityAction("Scroll up") {
                                if (state.connected) {
                                    actions.scroll(120)
                                    true
                                } else false
                            },
                            CustomAccessibilityAction("Scroll down") {
                                if (state.connected) {
                                    actions.scroll(-120)
                                    true
                                } else false
                            },
                        )
                }
                .pointerInput(state.connected, state.sensitivity) {
                    if (!state.connected) return@pointerInput
                    awaitEachGesture {
                        val down = awaitFirstDown(requireUnconsumed = false)
                        var distance = 0f
                        var twoFingers = false
                        var scrollRemainder = 0f
                        do {
                            val event = awaitPointerEvent()
                            val pressed = event.changes.filter { it.pressed }
                            twoFingers = twoFingers || pressed.size > 1
                            val delta =
                                event.changes
                                    .map { it.positionChange() }
                                    .fold(Offset.Zero) { acc, offset -> acc + offset } /
                                    event.changes.size.toFloat()
                            distance += abs(delta.x) + abs(delta.y)
                            if (pressed.size > 1) {
                                scrollRemainder -= delta.y * 4
                                val steps = scrollRemainder.toInt()
                                if (steps != 0) {
                                    actions.scroll(steps)
                                    scrollRemainder -= steps
                                }
                            } else if (!twoFingers && pressed.isNotEmpty() && delta != Offset.Zero)
                                actions.pointer(delta.x, delta.y)
                            event.changes.forEach { it.consume() }
                            if (pressed.isEmpty() && distance < viewConfiguration.touchSlop)
                                actions.click(
                                    twoFingers ||
                                        event.changes.first().uptimeMillis - down.uptimeMillis >
                                            viewConfiguration.longPressTimeoutMillis
                                )
                        } while (event.changes.any { it.pressed })
                    }
                },
            contentAlignment = Alignment.Center,
        ) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Icon(
                    Icons.Rounded.TouchApp,
                    null,
                    Modifier.size(40.dp),
                    tint = MaterialTheme.colorScheme.outline,
                )
                Text(
                    if (state.connected) "SPACE TO MOVE" else "CONNECT TO USE",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedButton(
                { actions.click(false) },
                Modifier.weight(1f),
                enabled = state.connected,
            ) {
                Text("Left click")
            }
            OutlinedButton(
                { actions.click(true) },
                Modifier.weight(1f),
                enabled = state.connected,
            ) {
                Text("Right click")
            }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
            Control(Icons.Rounded.KeyboardArrowUp, "Scroll up", state.connected) {
                actions.scroll(120)
            }
            Control(Icons.Rounded.Keyboard, "Open keyboard", state.connected, onClick = onKeyboard)
            Control(Icons.Rounded.KeyboardArrowDown, "Scroll down", state.connected) {
                actions.scroll(-120)
            }
        }
        Text("Pointer sensitivity", style = MaterialTheme.typography.titleMedium)
        Slider(
            state.sensitivity,
            actions.sensitivity,
            valueRange = .5f..3f,
            modifier = Modifier.semantics { contentDescription = "Pointer sensitivity" },
        )
    }
}

@Composable
private fun QueueScreen(state: AppState, actions: RemoteActions) {
    var add by rememberSaveable { mutableStateOf(false) }
    var clear by rememberSaveable { mutableStateOf(false) }
    Column(
        Modifier.fillMaxSize().padding(horizontal = 24.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Row(
            Modifier.fillMaxWidth().padding(top = 16.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(Modifier.weight(1f)) {
                Text("Up next.", style = MaterialTheme.typography.headlineMedium)
                Text(
                    "${state.remote.queue.size} videos · saved on your computer",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            IconButton({ add = true }) { Icon(Icons.Rounded.Add, "Add YouTube video") }
        }
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
            TextButton(
                { actions.queue("previous") },
                enabled = state.connected && state.remote.queue.isNotEmpty(),
            ) {
                Text("Previous")
            }
            TextButton(
                { actions.queue("next") },
                enabled = state.connected && state.remote.queue.isNotEmpty(),
            ) {
                Text("Next")
            }
            TextButton(
                { clear = true },
                enabled = state.connected && state.remote.queue.isNotEmpty(),
            ) {
                Text("Clear")
            }
        }
        if (state.remote.queue.isEmpty()) {
            Column(
                Modifier.fillMaxWidth().weight(1f),
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center,
            ) {
                Icon(
                    Icons.AutoMirrored.Rounded.QueueMusic,
                    null,
                    Modifier.size(48.dp),
                    tint = MaterialTheme.colorScheme.primary,
                )
                Spacer(Modifier.height(20.dp))
                Text(
                    if (state.connected) "Make an evening of it." else "Your queue will be here.",
                    style = MaterialTheme.typography.titleLarge,
                )
                Text(
                    if (state.connected) "Share a video from YouTube or paste its link."
                    else "Connect to sync the queue from your computer.",
                    modifier = Modifier.padding(16.dp),
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Button({ add = true }) { Text("Add a video") }
            }
        } else
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp),
                contentPadding = PaddingValues(bottom = 24.dp),
            ) {
                itemsIndexed(state.remote.queue, key = { _, item -> item.entryId }) { index, item ->
                    Surface(
                        modifier =
                            if (LocalReducedMotion.current) Modifier else Modifier.animateItem(),
                        shape = RoundedCornerShape(20.dp),
                        color =
                            if (item.entryId == state.remote.currentEntryId)
                                MaterialTheme.colorScheme.surfaceVariant
                            else MaterialTheme.colorScheme.surface,
                    ) {
                        Column(Modifier.fillMaxWidth().padding(16.dp)) {
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Text(
                                    if (item.entryId == state.remote.currentEntryId) "PLAYING"
                                    else "%02d".format(index + 1),
                                    style = MaterialTheme.typography.labelSmall,
                                    color = MaterialTheme.colorScheme.primary,
                                    modifier = Modifier.padding(end = 12.dp),
                                )
                                Text(
                                    item.title.ifBlank { item.videoId },
                                    style = MaterialTheme.typography.titleMedium,
                                    maxLines = 2,
                                    overflow = TextOverflow.Ellipsis,
                                    modifier = Modifier.weight(1f),
                                )
                            }
                            Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                                Control(
                                    Icons.Rounded.KeyboardArrowUp,
                                    "Move ${item.title} earlier",
                                    state.connected && index > 0,
                                ) {
                                    actions.move(item.entryId, index - 1)
                                }
                                Control(
                                    Icons.Rounded.KeyboardArrowDown,
                                    "Move ${item.title} later",
                                    state.connected && index < state.remote.queue.lastIndex,
                                ) {
                                    actions.move(item.entryId, index + 1)
                                }
                                Control(
                                    Icons.Rounded.Close,
                                    "Remove ${item.title}",
                                    state.connected,
                                ) {
                                    actions.remove(item.entryId)
                                }
                            }
                        }
                    }
                }
            }
    }
    if (add)
        VideoDialog(
            "",
            state.connected,
            actions.enqueue,
            onDismiss = { add = false },
            pending = state.videoPending,
            accepted = state.videoAccepted,
            acceptedUrl = state.videoAcceptedUrl,
            error = state.error,
        )
    if (clear)
        AlertDialog(
            onDismissRequest = { clear = false },
            title = { Text("Clear the queue?") },
            text = { Text("This removes every queued video from your computer.") },
            confirmButton = {
                TextButton({
                    actions.queue("clear")
                    clear = false
                }) {
                    Text("Clear queue")
                }
            },
            dismissButton = { TextButton({ clear = false }) { Text("Keep queue") } },
        )
}

@Composable
private fun DevicesScreen(state: AppState, actions: RemoteActions) {
    var add by rememberSaveable { mutableStateOf(false) }
    var discovered by remember { mutableStateOf<DiscoveredComputer?>(null) }
    var forget by remember { mutableStateOf<PairedComputer?>(null) }
    var editing by remember { mutableStateOf<PairedComputer?>(null) }
    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
    ) {
        Text("A familiar place.", style = MaterialTheme.typography.headlineMedium)
        Text(
            "Pair once. Settle in whenever you like.",
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (state.computers.isEmpty())
            Text(
                "On Windows, open Bedside Remote → Pair a phone. Keep both devices on the same private network.",
                modifier = Modifier.padding(vertical = 12.dp),
            )
        state.computers.forEach { computer ->
            Surface(shape = RoundedCornerShape(24.dp), color = MaterialTheme.colorScheme.surface) {
                Column(
                    Modifier.fillMaxWidth().padding(18.dp),
                    verticalArrangement = Arrangement.spacedBy(6.dp),
                ) {
                    Text(computer.name, style = MaterialTheme.typography.titleLarge)
                    Text(
                        "${computer.host}:${computer.port}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    if (computer.deviceId == state.selectedId)
                        Text(
                            statusLabel(state.status),
                            color = MaterialTheme.colorScheme.primary,
                            style = MaterialTheme.typography.bodySmall,
                        )
                    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
                        TextButton({ editing = computer }) { Text("Edit address") }
                        TextButton({ forget = computer }) { Text("Forget") }
                        FilledTonalButton({ actions.select(computer) }) {
                            Text(
                                if (computer.deviceId == state.selectedId && state.connected)
                                    "Reconnect"
                                else "Connect"
                            )
                        }
                    }
                }
            }
        }
        Button(
            {
                discovered = null
                add = true
            },
            Modifier.fillMaxWidth(),
        ) {
            Icon(Icons.Rounded.Add, null)
            Spacer(Modifier.width(8.dp))
            Text("Pair a computer manually")
        }
        Text(
            "NEARBY COMPUTERS",
            style = MaterialTheme.typography.labelSmall,
            modifier = Modifier.padding(top = 16.dp),
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        if (state.discovered.isEmpty())
            Text(
                "Looking on your local network…\nDiscovery may be blocked by Wi-Fi isolation or Windows Firewall. Manual pairing is always available.",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        state.discovered.forEach { computer ->
            OutlinedCard(
                onClick = {
                    discovered = computer
                    add = true
                },
                modifier = Modifier.fillMaxWidth(),
            ) {
                Row(
                    Modifier.padding(18.dp),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(16.dp),
                ) {
                    Icon(Icons.Rounded.DesktopWindows, null)
                    Column(Modifier.weight(1f)) {
                        Text(computer.name, style = MaterialTheme.typography.titleMedium)
                        Text(
                            "${computer.host}:${computer.port}",
                            style = MaterialTheme.typography.bodySmall,
                        )
                    }
                    Icon(Icons.Rounded.Add, "Pair ${computer.name}")
                }
            }
        }
        Text(
            "The certificate fingerprint must match Windows. Discovery never verifies a computer's identity.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
    if (add)
        PairDialog(
            state.pairing,
            state.error,
            discovered,
            actions.pair,
            onDismiss = {
                actions.cancelPairing()
                add = false
            },
        )
    LaunchedEffect(state.pairing, state.notice) {
        if (!state.pairing && state.notice == "Computer paired securely") add = false
    }
    editing?.let { computer ->
        EndpointDialog(computer, state.endpointSaving, state.error, actions.updateEndpoint) {
            editing = null
        }
    }
    LaunchedEffect(state.endpointSaving, state.computers) {
        if (
            !state.endpointSaving &&
                editing != null &&
                state.computers.firstOrNull { it.deviceId == editing?.deviceId } != editing
        )
            editing = null
    }
    forget?.let { computer ->
        AlertDialog(
            onDismissRequest = { forget = null },
            title = { Text("Forget ${computer.name}?") },
            text = {
                Text(
                    "Removes its credentials from this phone. Also revoke this phone in the Windows app to invalidate authorization."
                )
            },
            confirmButton = {
                TextButton({
                    actions.forget(computer)
                    forget = null
                }) {
                    Text("Forget computer")
                }
            },
            dismissButton = { TextButton({ forget = null }) { Text("Cancel") } },
        )
    }
}

@Composable
private fun EndpointDialog(
    computer: PairedComputer,
    saving: Boolean,
    error: String?,
    onSave: (PairedComputer, String, String) -> Unit,
    onDismiss: () -> Unit,
) {
    var host by rememberSaveable(computer.deviceId) { mutableStateOf(computer.host) }
    var port by rememberSaveable(computer.deviceId) { mutableStateOf(computer.port.toString()) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Update computer address") },
        text = {
            Column(
                Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text(
                    "Use the address shown in Windows. Its saved certificate is checked before saving; pairing stays intact."
                )
                OutlinedTextField(
                    host,
                    { host = it },
                    label = { Text("Computer IP address") },
                    enabled = !saving,
                    singleLine = true,
                )
                OutlinedTextField(
                    port,
                    { port = it },
                    label = { Text("Port") },
                    enabled = !saving,
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                )
                if (saving) LinearProgressIndicator(Modifier.fillMaxWidth())
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
            }
        },
        confirmButton = {
            TextButton({ onSave(computer, host, port) }, enabled = !saving && host.isNotBlank()) {
                Text("Verify and save")
            }
        },
        dismissButton = { TextButton(onDismiss) { Text("Cancel") } },
    )
}

@Composable
internal fun PairDialog(
    pairing: Boolean,
    error: String?,
    discovered: DiscoveredComputer?,
    onPair: (String, String, String, String, Boolean) -> Unit,
    onDismiss: () -> Unit,
) {
    var host by rememberSaveable(discovered?.host) { mutableStateOf(discovered?.host ?: "") }
    var port by
        rememberSaveable(discovered?.port) { mutableStateOf((discovered?.port ?: 8765).toString()) }
    var fingerprint by remember { mutableStateOf("") }
    var code by remember { mutableStateOf("") }
    var verified by remember { mutableStateOf(false) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Pair securely.") },
        text = {
            Column(
                Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                Text(
                    "Open pairing on Windows. Enter its IP address, full SHA-256 certificate fingerprint and temporary code. Then approve this phone on Windows."
                )
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                OutlinedTextField(
                    host,
                    { host = it },
                    label = { Text("Computer IP address") },
                    enabled = !pairing,
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                )
                OutlinedTextField(
                    port,
                    { port = it },
                    label = { Text("Port") },
                    enabled = !pairing,
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                )
                OutlinedTextField(
                    fingerprint,
                    {
                        fingerprint = it
                        verified = false
                    },
                    label = { Text("SHA-256 fingerprint from Windows") },
                    enabled = !pairing,
                    minLines = 2,
                    modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(autoCorrectEnabled = false),
                )
                Row(
                    Modifier.toggleable(
                        value = verified,
                        enabled = !pairing,
                        role = Role.Checkbox,
                        onValueChange = { verified = it },
                    ),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Checkbox(verified, null, enabled = !pairing)
                    Text(
                        "I compared all 64 fingerprint digits with Windows",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
                OutlinedTextField(
                    code,
                    { code = it.filter(Char::isDigit).take(6) },
                    label = { Text("Six-digit code") },
                    enabled = !pairing,
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    visualTransformation = PasswordVisualTransformation(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword),
                )
                if (pairing) {
                    LinearProgressIndicator(Modifier.fillMaxWidth())
                    Text(
                        "Waiting for approval on Windows…",
                        style = MaterialTheme.typography.bodySmall,
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                { onPair(host, port, fingerprint, code, verified) },
                enabled =
                    !pairing &&
                        verified &&
                        code.length == 6 &&
                        host.isNotBlank() &&
                        runCatching { LocalEndpoint.normalizeFingerprint(fingerprint) }.isSuccess,
            ) {
                Text("Pair computer")
            }
        },
        dismissButton = { TextButton(onDismiss) { Text("Cancel") } },
    )
}

@Composable
internal fun KeyboardDialog(state: AppState, actions: RemoteActions, onDismiss: () -> Unit) {
    var value by rememberSaveable { mutableStateOf("") }
    val acceptedAtOpen = remember { state.textAccepted }
    LaunchedEffect(state.textAccepted) { if (state.textAccepted != acceptedAtOpen) value = "" }
    val connected = state.connected && !state.textPending
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Say it with keys.") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
                Text(
                    "Focus a text field on your computer, then send text. Your Android keyboard supports accents, emoji and other Unicode characters.",
                    style = MaterialTheme.typography.bodyMedium,
                )
                OutlinedTextField(
                    value,
                    { value = it.take(4096) },
                    label = { Text("Text to send") },
                    minLines = 3,
                    maxLines = 6,
                    modifier = Modifier.fillMaxWidth(),
                    enabled = connected,
                )
                Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceEvenly) {
                    Control(Icons.AutoMirrored.Rounded.KeyboardReturn, "Enter key", connected) {
                        actions.key("enter")
                    }
                    Control(Icons.AutoMirrored.Rounded.Backspace, "Backspace key", connected) {
                        actions.key("backspace")
                    }
                    Control(Icons.AutoMirrored.Rounded.KeyboardTab, "Tab key", connected) {
                        actions.key("tab")
                    }
                    Control(Icons.Rounded.SpaceBar, "Space key", connected) { actions.key("space") }
                }
            }
        },
        confirmButton = {
            TextButton({ actions.text(value) }, enabled = connected && value.isNotBlank()) {
                Text(if (state.textPending) "Sending…" else "Send text")
            }
        },
        dismissButton = { TextButton(onDismiss) { Text("Done") } },
    )
}

@Composable
private fun VideoDialog(
    initialUrl: String,
    connected: Boolean,
    enqueue: (String, String) -> Unit,
    onDismiss: () -> Unit,
    onDevices: (() -> Unit)? = null,
    error: String? = null,
    pending: Boolean = false,
    accepted: Long = 0,
    acceptedUrl: String? = null,
) {
    val acceptedAtOpen = remember { accepted }
    var submitted by remember { mutableStateOf<String?>(null) }
    LaunchedEffect(accepted) {
        if (accepted != acceptedAtOpen && submitted != null && submitted == acceptedUrl) onDismiss()
    }
    var url by rememberSaveable(initialUrl) { mutableStateOf(initialUrl) }
    val valid = YoutubeLinks.canonical(url) != null
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("An evening, queued.") },
        text = {
            Column(
                Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                OutlinedTextField(
                    url,
                    { url = it },
                    label = { Text("YouTube video link") },
                    enabled = !pending,
                    modifier = Modifier.fillMaxWidth(),
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Uri),
                    isError = url.isNotBlank() && !valid,
                    supportingText = { Text("Official HTTPS YouTube video links only") },
                )
                if (pending) LinearProgressIndicator(Modifier.fillMaxWidth())
                error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
                if (!connected) {
                    Text("Connect to your computer before sending this video.")
                    onDevices?.let { TextButton(it) { Text("Choose a computer") } }
                }
                listOf("now" to "Play now", "next" to "Play next", "last" to "Add to queue")
                    .forEach { (mode, label) ->
                        Button(
                            {
                                submitted = YoutubeLinks.canonical(url)
                                enqueue(url, mode)
                            },
                            Modifier.fillMaxWidth(),
                            enabled = connected && valid && !pending,
                        ) {
                            Text(label)
                        }
                    }
            }
        },
        confirmButton = { TextButton(onDismiss) { Text("Close") } },
    )
}

@Composable
private fun SettingsDialog(state: AppState, actions: RemoteActions, onDismiss: () -> Unit) {
    val context = LocalContext.current
    var licenses by remember { mutableStateOf<String?>(null) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text("Make yourself comfortable.") },
        text = {
            Column(
                Modifier.verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                SettingToggle(
                    "Haptic feedback",
                    "Subtle touch feedback; off by default",
                    state.haptics,
                    actions.haptics,
                )
                SettingToggle(
                    "Reduce motion",
                    "Immediate transitions; system animation settings also apply",
                    state.reducedMotion,
                    actions.reducedMotion,
                )
                SettingToggle(
                    "Light appearance",
                    "Midnight appearance is the default",
                    state.lightTheme,
                    actions.lightTheme,
                )
                HorizontalDivider()
                Text(
                    "Bedside Remote ${BuildConfig.VERSION_NAME}\nProtocol 1",
                    style = MaterialTheme.typography.bodySmall,
                )
                TextButton({
                    runCatching {
                        context.startActivity(
                            Intent(
                                Intent.ACTION_VIEW,
                                Uri.parse(
                                    "https://github.com/hugo2006alm/bedside-remote/releases/latest"
                                ),
                            )
                        )
                    }
                }) {
                    Text("Check releases")
                }
                TextButton({
                    licenses =
                        context.assets.open("licenses/DM-Sans-OFL.txt").bufferedReader().use {
                            it.readText()
                        } +
                            "\n\n" +
                            context.assets.open("licenses/Manrope-OFL.txt").bufferedReader().use {
                                it.readText()
                            }
                }) {
                    Text("Font licenses")
                }
            }
        },
        confirmButton = { TextButton(onDismiss) { Text("Done") } },
    )
    licenses?.let { text ->
        AlertDialog(
            onDismissRequest = { licenses = null },
            title = { Text("Font licenses") },
            text = {
                Text(
                    text,
                    Modifier.verticalScroll(rememberScrollState()),
                    style = MaterialTheme.typography.bodySmall,
                )
            },
            confirmButton = { TextButton({ licenses = null }) { Text("Done") } },
        )
    }
}

@Composable
private fun SettingToggle(
    title: String,
    subtitle: String,
    checked: Boolean,
    onChange: (Boolean) -> Unit,
) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleMedium)
            Text(
                subtitle,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        Switch(checked, onChange, modifier = Modifier.semantics { contentDescription = title })
    }
}

@Preview(name = "Midnight remote", showBackground = true, widthDp = 393, heightDp = 852)
@Composable
private fun RemotePreview() {
    BedsideTheme { RemoteContent(AppState(status = ConnectionStatus.CONNECTED), RemoteActions()) }
}

@Preview(
    name = "Large text and daylight",
    showBackground = true,
    widthDp = 393,
    heightDp = 852,
    fontScale = 1.5f,
)
@Composable
private fun LargeTextPreview() {
    BedsideTheme(light = true) { RemoteContent(AppState(lightTheme = true), RemoteActions()) }
}
