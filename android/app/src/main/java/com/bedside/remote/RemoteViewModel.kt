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
    val error: String? = null,
    val notice: String? = null,
    val pairing: Boolean = false,
    val sharedUrl: String? = null,
    val haptics: Boolean = false,
    val reducedMotion: Boolean = false,
    val lightTheme: Boolean = false,
    val sensitivity: Float = 1.5f,
) {
    val connected: Boolean
        get() = status == ConnectionStatus.CONNECTED

    val selected: PairedComputer?
        get() = computers.firstOrNull { it.deviceId == selectedId }
}

class RemoteViewModel(application: Application) : AndroidViewModel(application) {
    private val store = SecureStore(application)
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
                    )
                }
            },
            onState = { remote -> mutableState.update { it.copy(remote = remote) } },
            onError = { error -> mutableState.update { it.copy(error = error) } },
            onAccepted = { action ->
                if (action == "youtube.enqueue")
                    mutableState.update {
                        it.copy(
                            notice =
                                "Video accepted by your computer. Check playback for autoplay restrictions.",
                            sharedUrl = null,
                        )
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
        viewModelScope.launch(Dispatchers.IO) {
            runCatching { store.computers() }
                .onSuccess { devices ->
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
                    withContext(Dispatchers.Main) {
                        if (foreground) devices.firstOrNull()?.let(transport::connect)
                    }
                }
                .onFailure { failure ->
                    mutableState.update {
                        it.copy(
                            error =
                                "Stored pairing credentials cannot be opened. Clear app storage and pair again. ${failure.javaClass.simpleName}"
                        )
                    }
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
            val remaining =
                mutableState.value.computers.filterNot { it.deviceId == computer.deviceId }
            try {
                withContext(Dispatchers.IO) { store.save(remaining) }
                val selected = mutableState.value.selectedId == computer.deviceId
                if (selected) transport.disconnect()
                mutableState.update {
                    it.copy(
                        computers = remaining,
                        selectedId =
                            if (selected) remaining.firstOrNull()?.deviceId else it.selectedId,
                        notice =
                            "Computer forgotten on this phone. Revoke the phone in the Windows app to invalidate its token.",
                    )
                }
                if (selected && foreground) remaining.firstOrNull()?.let(transport::connect)
            } catch (error: Exception) {
                showError(error.message ?: "Could not forget computer")
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
                    val devices =
                        mutableState.value.computers.filterNot {
                            it.host == computer.host && it.port == computer.port
                        } + computer
                    withContext(Dispatchers.IO) { store.save(devices) }
                    mutableState.update {
                        it.copy(
                            computers = devices,
                            selectedId = computer.deviceId,
                            pairing = false,
                            notice = "Computer paired securely",
                        )
                    }
                    if (foreground) transport.connect(computer)
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
        if (value.isBlank()) return
        if (value.length > 4096) {
            showError("Send up to 4096 characters at a time")
            return
        }
        command("input.text", "text" to JsonPrimitive(value))
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
        val canonical =
            YoutubeLinks.canonical(url)
                ?: run {
                    showError("Enter a valid official HTTPS YouTube video link")
                    return
                }
        command("youtube.enqueue", "url" to JsonPrimitive(canonical), "mode" to JsonPrimitive(mode))
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
