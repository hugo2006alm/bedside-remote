package com.bedside.remote

import android.annotation.SuppressLint
import java.io.IOException
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import java.util.UUID
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import okhttp3.Call
import okhttp3.Callback
import okhttp3.Dns
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString

enum class ConnectionStatus {
    DISCONNECTED,
    CONNECTING,
    CONNECTED,
    RECONNECTING,
    REVOKED,
}

class RemoteTransport(
    private val scope: CoroutineScope,
    private val onStatus: (ConnectionStatus) -> Unit,
    private val onState: (RemoteState) -> Unit,
    private val onError: (String) -> Unit,
    private val onAccepted: (String) -> Unit,
) {
    private var socket: WebSocket? = null
    private var client: OkHttpClient? = null
    private var retry: Job? = null
    private var computer: PairedComputer? = null
    private var generation = 0L
    private var sequence = 0L
    private var failures = 0
    private var connected = false

    private data class PendingCommand(val timeout: Job, val action: String)

    private val pending = mutableMapOf<String, PendingCommand>()

    companion object {
        @SuppressLint(
            "CustomX509TrustManager"
        ) // Self-signed identity uses a user-verified DER pin; TLS tests prove mismatched pins send
        // no secret.
        fun pinnedClient(fingerprint: String): OkHttpClient {
            val normalized = LocalEndpoint.normalizeFingerprint(fingerprint)
            val trust =
                object : X509TrustManager {
                    override fun checkClientTrusted(
                        chain: Array<out X509Certificate>?,
                        authType: String?,
                    ) = throw CertificateException("Client certificates are unsupported")

                    override fun checkServerTrusted(
                        chain: Array<out X509Certificate>?,
                        authType: String?,
                    ) {
                        val leaf =
                            chain?.firstOrNull()
                                ?: throw CertificateException("Missing server certificate")
                        leaf.checkValidity()
                        if (!LocalEndpoint.matchesPin(leaf, normalized))
                            throw CertificateException(
                                "Computer fingerprint mismatch; check Windows before pairing again"
                            )
                    }

                    override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
                }
            val context = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trust), null) }
            return OkHttpClient.Builder()
                .sslSocketFactory(context.socketFactory, trust)
                // A user-verified certificate pin establishes this self-signed computer's identity.
                .hostnameVerifier { _, session ->
                    runCatching {
                            LocalEndpoint.matchesPin(
                                session.peerCertificates.first() as X509Certificate,
                                normalized,
                            )
                        }
                        .getOrDefault(false)
                }
                .dns(
                    object : Dns {
                        override fun lookup(hostname: String) =
                            listOf(LocalEndpoint.address(hostname))
                    }
                )
                .followRedirects(false)
                .followSslRedirects(false)
                .connectTimeout(10, TimeUnit.SECONDS)
                .readTimeout(5, TimeUnit.MINUTES)
                .callTimeout(5, TimeUnit.MINUTES)
                .pingInterval(20, TimeUnit.SECONDS)
                .build()
        }

        suspend fun pair(
            host: String,
            port: Int,
            fingerprint: String,
            code: String,
            deviceName: String,
        ): PairedComputer {
            require(code.matches(Regex("[0-9]{6}"))) { "Enter the six-digit pairing code" }
            require(deviceName.isNotBlank() && deviceName.length <= 80) {
                "Device name must be 1–80 characters"
            }
            val base = LocalEndpoint.baseUrl(host, port)
            val pin = LocalEndpoint.normalizeFingerprint(fingerprint)
            val client = pinnedClient(pin)
            try {
                val body =
                    WireJson.encodeToString(PairRequest(deviceName = deviceName, code = code))
                        .toRequestBody("application/json".toMediaType())
                val request = Request.Builder().url("$base/pair").post(body).build()
                val response = suspendCancellableCoroutine { continuation ->
                    val call = client.newCall(request)
                    continuation.invokeOnCancellation { call.cancel() }
                    call.enqueue(
                        object : Callback {
                            override fun onFailure(call: Call, e: IOException) {
                                if (continuation.isActive) continuation.resumeWithException(e)
                            }

                            override fun onResponse(call: Call, response: Response) {
                                if (continuation.isActive) continuation.resume(response)
                                else response.close()
                            }
                        }
                    )
                }
                response.use {
                    require(it.isSuccessful) {
                        when (it.code) {
                            429 ->
                                "Too many pairing attempts. Wait, then open pairing on Windows again."
                            403 ->
                                "Pairing refused or expired. Check the code and approve on Windows."
                            else ->
                                "Pairing failed (${it.code}). Open pairing and approve this phone on Windows."
                        }
                    }
                    val source = it.body?.source() ?: error("Empty pairing response")
                    source.request(65537)
                    require(source.buffer.size <= 65536) { "Oversized pairing response" }
                    val bytes = source.readByteArray()
                    val paired =
                        WireJson.decodeFromString<PairResponse>(bytes.toString(Charsets.UTF_8))
                    require(
                        paired.version == 1 &&
                            paired.deviceId.isNotBlank() &&
                            paired.token.length in 32..512
                    ) {
                        "Invalid pairing response"
                    }
                    return PairedComputer(
                        paired.deviceId,
                        paired.deviceName,
                        host.trim(),
                        port,
                        pin,
                        paired.token,
                    )
                }
            } finally {
                client.connectionPool.evictAll()
                client.dispatcher.executorService.shutdown()
            }
        }
    }

    @Synchronized
    fun connect(target: PairedComputer) {
        disconnect()
        computer = target
        failures = 0
        open()
    }

    @Synchronized
    private fun open() {
        val target = computer ?: return
        val current = ++generation
        onStatus(if (failures == 0) ConnectionStatus.CONNECTING else ConnectionStatus.RECONNECTING)
        try {
            val base = LocalEndpoint.baseUrl(target.host, target.port)
            val http = pinnedClient(target.fingerprint)
            client = http
            sequence = 0
            socket =
                http.newWebSocket(
                    Request.Builder()
                        .url("${base.replaceFirst("https", "wss")}/ws")
                        .header("Authorization", "Bearer ${target.token}")
                        .build(),
                    object : WebSocketListener() {
                        override fun onOpen(webSocket: WebSocket, response: Response) =
                            synchronized(this@RemoteTransport) {
                                if (generation != current) {
                                    webSocket.cancel()
                                    return@synchronized
                                }
                                connected = true
                                failures = 0
                                onStatus(ConnectionStatus.CONNECTED)
                            }

                        override fun onMessage(webSocket: WebSocket, text: String) =
                            synchronized(this@RemoteTransport) {
                                if (generation != current) return@synchronized
                                if (text.toByteArray().size > 65536) {
                                    webSocket.close(1009, "Message too large")
                                    return@synchronized
                                }
                                runCatching {
                                        val envelope = WireJson.parseToJsonElement(text).jsonObject
                                        require(
                                            envelope["version"]?.jsonPrimitive?.content == "1"
                                        ) {
                                            "Unsupported agent protocol"
                                        }
                                        when (envelope["type"]?.jsonPrimitive?.content) {
                                            "state" ->
                                                onState(
                                                    WireJson.decodeFromString<RemoteState>(text)
                                                        .validate()
                                                )
                                            "ack" -> {
                                                val ack =
                                                    WireJson.decodeFromString<Acknowledgement>(text)
                                                val accepted =
                                                    pending.remove(ack.id)?.also {
                                                        it.timeout.cancel()
                                                    }
                                                if (accepted != null) {
                                                    if (!ack.ok)
                                                        onError(
                                                            ack.error?.take(300)
                                                                ?: "Computer could not execute that action"
                                                        )
                                                    else onAccepted(accepted.action)
                                                }
                                            }
                                            else -> error("Unsupported agent message")
                                        }
                                    }
                                    .onFailure {
                                        onError(it.message?.take(300) ?: "Invalid agent state")
                                        webSocket.close(1002, "Invalid protocol message")
                                    }
                            }

                        override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
                            webSocket.close(1003, "Text JSON required")
                        }

                        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                            webSocket.close(code, reason)
                        }

                        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                            lost(current, code == 1008)
                        }

                        override fun onFailure(
                            webSocket: WebSocket,
                            t: Throwable,
                            response: Response?,
                        ) {
                            if (generation == current)
                                onError(
                                    if (response?.code == 401 || response?.code == 403)
                                        "Device authorization expired or was revoked. Pair again on Windows."
                                    else
                                        "Connection lost. Check the computer, LAN and certificate fingerprint."
                                )
                            lost(current, response?.code == 401 || response?.code == 403)
                            response?.close()
                        }
                    },
                )
        } catch (error: Exception) {
            onError(error.message ?: "Connection failed")
            lost(current, true)
        }
    }

    @Synchronized
    private fun lost(current: Long, revoked: Boolean) {
        if (current != generation) return
        connected = false
        pending.values.forEach { it.timeout.cancel() }
        pending.clear()
        client?.connectionPool?.evictAll()
        client?.dispatcher?.executorService?.shutdown()
        if (revoked) {
            computer = null
            onStatus(ConnectionStatus.REVOKED)
            return
        }
        failures++
        onStatus(ConnectionStatus.RECONNECTING)
        retry?.cancel()
        retry =
            scope.launch {
                delay((1000L shl failures.coerceAtMost(5)).coerceAtMost(30000))
                open()
            }
    }

    @Synchronized
    fun send(action: String, payload: JsonObject): String? {
        if (!connected) {
            onError("Connect to your computer first")
            return null
        }
        if (pending.size >= 100) {
            onError("Computer is busy. Wait for pending actions to finish.")
            return null
        }
        val id = UUID.randomUUID().toString()
        val command =
            RemoteCommand(id = id, sequence = ++sequence, action = action, payload = payload)
        if (socket?.send(WireJson.encodeToString(command)) != true) {
            onError("Command could not be sent. Try again after reconnecting.")
            return null
        }
        val timeout =
            scope.launch {
                delay(
                    if (
                        action.startsWith("browser.") ||
                            action.startsWith("queue.") ||
                            action == "youtube.enqueue"
                    )
                        25000
                    else 10000
                )
                synchronized(this@RemoteTransport) {
                    if (pending.remove(id) != null)
                        onError(
                            "Computer did not acknowledge the action. Check its state before retrying."
                        )
                }
            }
        pending[id] = PendingCommand(timeout, action)
        return id
    }

    @Synchronized
    fun disconnect() {
        ++generation
        computer = null
        connected = false
        retry?.cancel()
        pending.values.forEach { it.timeout.cancel() }
        pending.clear()
        socket?.close(1000, "App inactive")
        socket = null
        client?.connectionPool?.evictAll()
        client?.dispatcher?.executorService?.shutdown()
        client = null
        onStatus(ConnectionStatus.DISCONNECTED)
    }
}
