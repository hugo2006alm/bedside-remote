package com.bedside.remote

import android.app.Application
import android.os.Build
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

data class AppState(
    val computers: List<PairedComputer> = emptyList(),
    val discovered: List<DiscoveredComputer> = emptyList(),
    val selectedId: String? = null,
    val status: ConnectionStatus = ConnectionStatus.DISCONNECTED,
    val remote: RemoteState = RemoteState(),
    val catalog: AgentCatalog = AgentCatalog(),
    val error: String? = null,
    val notice: String? = null,
    val pairing: Boolean = false,
    val sharedUrl: String? = null,
    val haptics: Boolean = false,
    val reducedMotion: Boolean = false,
    val lightTheme: Boolean = false,
    val sensitivity: Float = 1.5f,
    val textPending: Boolean = false,
    val textAccepted: Long = 0,
    val videoPending: Boolean = false,
    val videoAccepted: Long = 0,
    val videoAcceptedUrl: String? = null,
    val endpointSaving: Boolean = false,
    val dragRejected: Long = 0,
) {
    val connected: Boolean
        get() = status == ConnectionStatus.CONNECTED

    val selected: PairedComputer?
        get() = computers.firstOrNull { it.deviceId == selectedId }
}

class RemoteViewModel(application: Application) : AndroidViewModel(application) {
    private val store = SecureStore(application)
    private val computers = ComputerRepository(store::computers, store::save)
    private var submittedVideo: String? = null
    private val mutableState = MutableStateFlow(AppState())
    val state = mutableState.asStateFlow()
    private var foreground = false
    private var pairingJob: Job? = null
    private val transport =
        RemoteTransport(
            viewModelScope,
            onStatus = { status ->
                mutableState.update {
                    it.copy(
                        status = status,
                        remote =
                            if (status == ConnectionStatus.CONNECTED) it.remote else RemoteState(),
                        catalog =
                            if (status == ConnectionStatus.CONNECTED) it.catalog else AgentCatalog(),
                    )
                }
            },
            onState = { remote -> mutableState.update { it.copy(remote = remote) } },
            onCatalog = { catalog -> mutableState.update { it.copy(catalog = catalog) } },
            onError = { error -> mutableState.update { it.copy(error = error) } },
            onAccepted = {},
            onResult = { _, action, ok ->
                mutableState.update {
                    when (action) {
                        "input.drag" -> if (ok) it else it.copy(dragRejected = it.dragRejected + 1)
                        "input.text" ->
                            it.copy(
                                textPending = false,
                                textAccepted = it.textAccepted + if (ok) 1 else 0,
                            )
                        "youtube.enqueue" ->
                            it.copy(
                                videoPending = false,
                                videoAccepted = it.videoAccepted + if (ok) 1 else 0,
                                videoAcceptedUrl = if (ok) submittedVideo else it.videoAcceptedUrl,
                                sharedUrl =
                                    if (ok && it.sharedUrl == submittedVideo) null
                                    else it.sharedUrl,
                                notice =
                                    if (ok)
                                        "Video accepted by your computer. Check playback for autoplay restrictions."
                                    else it.notice,
                            )
                        else -> it
                    }
                }
            },
        )
    private val discovery =
        ComputerDiscovery(
            application,
            onDevices = { devices -> mutableState.update { it.copy(discovered = devices) } },
            onError = { error -> mutableState.update { it.copy(error = error) } },
        )

    init {
        viewModelScope.launch {
            try {
                computers.load { devices ->
                    mutableState.update {
                        it.copy(
                            computers = devices,
                            selectedId = devices.firstOrNull()?.deviceId,
                            haptics = store.haptics,
                            reducedMotion = store.reducedMotion,
                            lightTheme = store.lightTheme,
                            sensitivity = store.sensitivity,
                        )
                    }
                }
                if (foreground) mutableState.value.selected?.let(transport::connect)
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (failure: Exception) {
                showError(
                    "Stored pairing credentials cannot be opened. Clear app storage and pair again. ${failure.javaClass.simpleName}"
                )
            }
        }
    }

    fun onForeground() {
        foreground = true
        discovery.start()
        mutableState.value.selected?.let(transport::connect)
    }

    fun onBackground() {
        foreground = false
        discovery.stop()
        transport.disconnect()
    }

    fun select(computer: PairedComputer) {
        mutableState.update { it.copy(selectedId = computer.deviceId, error = null) }
        transport.connect(computer)
    }

    fun forget(computer: PairedComputer) =
        viewModelScope.launch {
            try {
                computers.update(
                    { current -> current.filterNot { it.deviceId == computer.deviceId } },
                    { remaining ->
                        val selected = mutableState.value.selectedId == computer.deviceId
                        if (selected) transport.disconnect()
                        mutableState.update {
                            it.copy(
                                computers = remaining,
                                selectedId =
                                    if (selected) remaining.firstOrNull()?.deviceId
                                    else it.selectedId,
                                notice =
                                    "Computer forgotten on this phone. Revoke the phone in the Windows app to invalidate its token.",
                            )
                        }
                        if (selected && foreground) remaining.firstOrNull()?.let(transport::connect)
                    },
                )
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: Exception) {
                showError(error.message ?: "Could not forget computer")
            }
        }

    fun updateEndpoint(computer: PairedComputer, host: String, port: String) {
        if (mutableState.value.endpointSaving) return
        mutableState.update { it.copy(endpointSaving = true, error = null) }
        viewModelScope.launch {
            try {
                val updated = computer.copy(host = host.trim(), port = port.toIntOrNull() ?: 0)
                LocalEndpoint.baseUrl(updated.host, updated.port)
                transport.verifyEndpoint(updated)
                computers.update(
                    { current ->
                        require(current.any { it.deviceId == computer.deviceId }) {
                            "Computer was forgotten. Pair it again."
                        }
                        current.map {
                            if (it.deviceId == computer.deviceId)
                                it.copy(host = updated.host, port = updated.port)
                            else it
                        }
                    },
                    { saved ->
                        mutableState.update {
                            it.copy(
                                computers = saved,
                                notice = "Computer address verified and saved",
                            )
                        }
                        if (foreground && mutableState.value.selectedId == computer.deviceId)
                            saved.first { it.deviceId == computer.deviceId }.let(transport::connect)
                    },
                )
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (error: Exception) {
                showError(error.message?.take(300) ?: "Could not verify the new address")
            } finally {
                mutableState.update { it.copy(endpointSaving = false) }
            }
        }
    }

    fun pair(host: String, port: String, fingerprint: String, code: String, verified: Boolean) {
        if (mutableState.value.pairing) return
        if (!verified) {
            showError(
                "Compare the full certificate fingerprint with the Windows pairing screen first"
            )
            return
        }
        mutableState.update {
            it.copy(
                pairing = true,
                error = null,
                notice = "Approve this phone in the Windows pairing window.",
            )
        }
        pairingJob =
            viewModelScope.launch {
                try {
                    val computer =
                        withContext(Dispatchers.IO) {
                            RemoteTransport.pair(
                                host,
                                port.toIntOrNull() ?: 0,
                                fingerprint,
                                code,
                                Build.MODEL.take(80),
                            )
                        }
                    computers.update(
                        { current ->
                            current.filterNot {
                                it.fingerprint == computer.fingerprint ||
                                    (it.host == computer.host && it.port == computer.port)
                            } + computer
                        },
                        { saved ->
                            mutableState.update {
                                it.copy(
                                    computers = saved,
                                    selectedId = computer.deviceId,
                                    pairing = false,
                                    notice = "Computer paired securely",
                                )
                            }
                            if (foreground) transport.connect(computer)
                        },
                    )
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (error: Exception) {
                    mutableState.update {
                        it.copy(error = error.message?.take(300) ?: "Pairing failed", notice = null)
                    }
                } finally {
                    mutableState.update { it.copy(pairing = false) }
                }
            }
    }

    fun cancelPairing() {
        pairingJob?.cancel()
        mutableState.update { it.copy(pairing = false, notice = null) }
    }

    fun receiveShare(text: String) {
        val url = YoutubeLinks.fromShare(text)
        if (url == null)
            showError("Share an HTTPS video link from the official YouTube app or website")
        else mutableState.update { it.copy(sharedUrl = url) }
    }

    fun dismissShare() {
        mutableState.update { it.copy(sharedUrl = null) }
    }

    fun dismissError() {
        mutableState.update { it.copy(error = null) }
    }

    fun dismissNotice() {
        mutableState.update { it.copy(notice = null) }
    }

    fun showError(message: String) {
        mutableState.update { it.copy(error = message) }
    }

    private fun command(action: String, vararg fields: Pair<String, JsonPrimitive>) =
        transport.send(action, JsonObject(mapOf(*fields)))

    fun key(key: String) {
        command("input.key", "key" to JsonPrimitive(key))
    }

    fun navigate(key: String) {
        if (mutableState.value.remote.activeSessionId != null)
            command("browser.navigate", "key" to JsonPrimitive(key))
        else desktopNavigate(key)
    }

    fun desktopNavigate(key: String) {
        if ("desktopNavigation" in mutableState.value.catalog.controls)
            command("input.navigate", "key" to JsonPrimitive(key))
        else
            showError(
                "Update the Windows agent to use icon navigation. Touchpad and keyboard remain available."
            )
    }

    fun launchApp(appId: String) {
        if (mutableState.value.catalog.apps.none { it.appId == appId }) return
        command("app.launch", "appId" to JsonPrimitive(appId))
    }

    fun drag(phase: String, dx: Float, dy: Float) {
        if ("drag" !in mutableState.value.catalog.controls) return
        command(
            "input.drag",
            "phase" to JsonPrimitive(phase),
            "dx" to JsonPrimitive((dx * mutableState.value.sensitivity).coerceIn(-1000f, 1000f)),
            "dy" to JsonPrimitive((dy * mutableState.value.sensitivity).coerceIn(-1000f, 1000f)),
        )
    }

    fun media(command: String) {
        this.command("media.command", "command" to JsonPrimitive(command))
    }

    fun browser(command: String, position: Double? = null) {
        if (command !in (mutableState.value.remote.playback?.capabilities ?: emptyList())) {
            showError("This browser action is unavailable. Use the universal remote.")
            return
        }
        if (position != null)
            this.command(
                "browser.command",
                "command" to JsonPrimitive(command),
                "position" to JsonPrimitive(position.coerceAtLeast(0.0)),
            )
        else this.command("browser.command", "command" to JsonPrimitive(command))
    }

    fun session(id: String) {
        command("browser.select", "sessionId" to JsonPrimitive(id))
    }

    fun text(value: String) {
        if (value.isBlank() || mutableState.value.textPending) return
        if (value.length > 4096) {
            showError("Send up to 4096 characters at a time")
            return
        }
        mutableState.update { it.copy(textPending = true) }
        if (command("input.text", "text" to JsonPrimitive(value)) == null)
            mutableState.update { it.copy(textPending = false) }
    }

    fun pointer(dx: Float, dy: Float) {
        command(
            "input.pointer",
            "dx" to JsonPrimitive((dx * mutableState.value.sensitivity).coerceIn(-1000f, 1000f)),
            "dy" to JsonPrimitive((dy * mutableState.value.sensitivity).coerceIn(-1000f, 1000f)),
        )
    }

    fun click(right: Boolean = false) {
        command("input.click", "button" to JsonPrimitive(if (right) "right" else "left"))
    }

    fun scroll(delta: Int) {
        if (delta != 0)
            command("input.scroll", "delta" to JsonPrimitive(delta.coerceIn(-1200, 1200)))
    }

    fun enqueue(url: String, mode: String) {
        if (mutableState.value.videoPending) return
        val canonical =
            YoutubeLinks.canonical(url)
                ?: run {
                    showError("Enter a valid official HTTPS YouTube video link")
                    return
                }
        submittedVideo = canonical
        mutableState.update { it.copy(videoPending = true) }
        if (
            command(
                "youtube.enqueue",
                "url" to JsonPrimitive(canonical),
                "mode" to JsonPrimitive(mode),
            ) == null
        )
            mutableState.update { it.copy(videoPending = false) }
    }

    fun queue(action: String) {
        command("queue.$action")
    }

    fun remove(entryId: String) {
        command("queue.remove", "entryId" to JsonPrimitive(entryId))
    }

    fun move(entryId: String, index: Int) {
        command("queue.move", "entryId" to JsonPrimitive(entryId), "index" to JsonPrimitive(index))
    }

    fun haptics(value: Boolean) {
        store.haptics = value
        mutableState.update { it.copy(haptics = value) }
    }

    fun reducedMotion(value: Boolean) {
        store.reducedMotion = value
        mutableState.update { it.copy(reducedMotion = value) }
    }

    fun lightTheme(value: Boolean) {
        store.lightTheme = value
        mutableState.update { it.copy(lightTheme = value) }
    }

    fun sensitivity(value: Float) {
        store.sensitivity = value
        mutableState.update { it.copy(sensitivity = value) }
    }

    override fun onCleared() {
        discovery.stop()
        transport.disconnect()
        super.onCleared()
    }
}
