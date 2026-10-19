package com.bedside.remote

import java.net.InetAddress
import java.net.URI
import java.net.URLDecoder
import java.security.MessageDigest
import java.security.cert.X509Certificate
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject

val WireJson = Json {
    ignoreUnknownKeys = false
    explicitNulls = false
    encodeDefaults = true
}

@Serializable
data class PairedComputer(
    val deviceId: String,
    val name: String,
    val host: String,
    val port: Int,
    val fingerprint: String,
    val token: String,
)

@Serializable
data class PairRequest(val version: Int = 1, val deviceName: String, val code: String)

@Serializable
data class PairResponse(
    val version: Int,
    val deviceId: String,
    val token: String,
    val deviceName: String,
)

@Serializable
data class RemoteCommand(
    val version: Int = 1,
    val type: String = "command",
    val id: String,
    val sequence: Long,
    val action: String,
    val payload: JsonObject,
)

@Serializable
data class Acknowledgement(
    val version: Int,
    val type: String,
    val id: String,
    val ok: Boolean,
    val error: String? = null,
)

@Serializable data class MediaApp(val appId: String, val name: String, val kind: String)

@Serializable
data class AgentCatalog(
    val version: Int = 1,
    val controls: List<String> = emptyList(),
    val apps: List<MediaApp> = emptyList(),
) {
    fun validate(): AgentCatalog {
        require(version == 1 && apps.size <= 5 && controls.size <= 8)
        require(apps.map { it.appId }.distinct().size == apps.size)
        require(
            apps.all {
                it.appId in setOf("youtube", "netflix", "spotify", "stremio", "vlc") &&
                    it.name.length in 1..64 &&
                    it.kind in setOf("browser", "app")
            }
        )
        return this
    }
}

@Serializable
data class RemoteState(
    val version: Int = 1,
    val type: String = "state",
    val deviceName: String = "Your computer",
    val extensionConnected: Boolean = false,
    val activeSessionId: String? = null,
    val sessions: List<BrowserSession> = emptyList(),
    val playback: Playback? = null,
    val queue: List<QueueEntry> = emptyList(),
    val currentEntryId: String? = null,
) {
    fun validate(): RemoteState {
        require(version == 1 && type == "state") { "Unsupported agent protocol" }
        require(queue.size <= 500 && sessions.size <= 100) { "Agent state exceeds limits" }
        require(queue.map { it.entryId }.distinct().size == queue.size)
        require(sessions.map { it.sessionId }.distinct().size == sessions.size)
        require(sessions.all { it.platform in setOf("youtube", "netflix") })
        require(activeSessionId == null || sessions.any { it.sessionId == activeSessionId })
        require(currentEntryId == null || queue.any { it.entryId == currentEntryId })
        require(
            queue.all {
                it.videoId.matches(Regex("[A-Za-z0-9_-]{11}")) &&
                    YoutubeLinks.canonical(it.url) ==
                        "https://www.youtube.com/watch?v=${it.videoId}"
            }
        )
        playback?.let {
            require(it.platform in setOf("youtube", "netflix"))
            require(
                it.position.isFinite() &&
                    it.duration.isFinite() &&
                    it.position >= 0 &&
                    it.duration >= 0
            )
            require(it.capabilities.all { capability -> capability in browserCommands })
        }
        return this
    }
}

val browserCommands =
    setOf(
        "playPause",
        "seek",
        "skipIntro",
        "skipRecap",
        "nextEpisode",
        "fullscreen",
        "subtitles",
        "audio",
    )

@Serializable
data class BrowserSession(
    val sessionId: String,
    val tabId: Int,
    val platform: String,
    val title: String,
)

@Serializable
data class Playback(
    val platform: String,
    val tabId: Int,
    val videoId: String? = null,
    val title: String,
    val channel: String? = null,
    val thumbnail: String? = null,
    val paused: Boolean,
    val position: Double,
    val duration: Double,
    val ended: Boolean,
    val capabilities: List<String>,
    val error: String? = null,
)

@Serializable
data class QueueEntry(val entryId: String, val videoId: String, val url: String, val title: String)

object YoutubeLinks {
    private val idPattern = Regex("[A-Za-z0-9_-]{11}")
    private val hosts =
        setOf("youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com", "youtu.be")

    fun canonical(value: String): String? =
        runCatching {
                val uri = URI(value.trim())
                require(uri.scheme.equals("https", ignoreCase = true) && uri.rawUserInfo == null)
                require(uri.port == -1 || uri.port == 443)
                val host = uri.host?.lowercase() ?: error("Missing host")
                require(host in hosts)
                val path = uri.rawPath
                val id =
                    if (host == "youtu.be") {
                        path.removePrefix("/").also { require(!it.contains('/')) }
                    } else if (path == "/watch") {
                        val matches =
                            (uri.rawQuery ?: "")
                                .split('&')
                                .map { it.split('=', limit = 2) }
                                .filter { it.first() == "v" }
                        require(matches.size == 1 && matches[0].size == 2)
                        URLDecoder.decode(matches[0][1], "UTF-8")
                    } else {
                        val parts = path.split('/')
                        require(parts.size == 3 && parts[1] in setOf("shorts", "live", "embed"))
                        parts[2]
                    }
                require(idPattern.matches(id))
                "https://www.youtube.com/watch?v=$id"
            }
            .getOrNull()

    fun fromShare(text: String): String? {
        canonical(text)?.let {
            return it
        }
        return Regex("https://[^\\s<>]+", RegexOption.IGNORE_CASE)
            .findAll(text)
            .mapNotNull { canonical(it.value) }
            .firstOrNull()
    }
}

object LocalEndpoint {
    fun address(host: String): InetAddress {
        val plain = host.trim().removeSurrounding("[", "]")
        require(plain.contains(':') || plain.matches(Regex("(?:[0-9]{1,3}\\.){3}[0-9]{1,3}"))) {
            "Use a local numeric IP address"
        }
        if (!plain.contains(':'))
            require(
                plain.split('.').all { it.toInt() in 0..255 && (it == "0" || !it.startsWith('0')) }
            ) {
                "Invalid IPv4 address"
            }
        val ip = InetAddress.getByName(plain)
        val bytes = ip.address
        val local =
            ip.isLoopbackAddress ||
                (bytes.size == 16 && ip.isLinkLocalAddress) ||
                (bytes.size == 4 &&
                    ((bytes[0].toInt() and 255) == 10 ||
                        ((bytes[0].toInt() and 255) == 172 &&
                            (bytes[1].toInt() and 255) in 16..31) ||
                        ((bytes[0].toInt() and 255) == 192 &&
                            (bytes[1].toInt() and 255) == 168))) ||
                (bytes.size == 16 && (bytes[0].toInt() and 254) == 252)
        require(local && !ip.isAnyLocalAddress && !ip.isMulticastAddress) {
            "Only private LAN addresses are allowed"
        }
        return ip
    }

    fun baseUrl(host: String, port: Int): String {
        require(port in 1..65535) { "Port must be 1–65535" }
        address(host)
        val clean = host.trim().removeSurrounding("[", "]")
        // OkHttp's URL grammar cannot carry an IPv6 zone. DNS supplies the scoped route.
        if ('%' in clean) return "https://bedside.invalid:$port"
        return "https://${if (clean.contains(':')) "[$clean]" else clean}:$port"
    }

    fun normalizeFingerprint(value: String): String {
        val normalized = value.replace(Regex("[\\s:]"), "").lowercase()
        require(normalized.matches(Regex("[a-f0-9]{64}"))) {
            "Enter all 64 SHA-256 fingerprint digits shown on Windows"
        }
        return normalized
    }

    fun matchesPin(certificate: X509Certificate, fingerprint: String): Boolean {
        val digest = MessageDigest.getInstance("SHA-256").digest(certificate.encoded)
        val expected = fingerprint.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
        return MessageDigest.isEqual(digest, expected)
    }
}
