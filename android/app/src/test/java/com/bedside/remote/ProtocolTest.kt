package com.bedside.remote

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import okhttp3.HttpUrl.Companion.toHttpUrl
import org.junit.Assert.*
import org.junit.Test

class ProtocolTest {
    @Serializable data class UrlCase(val url: String, val videoId: String?)

    @Test
    fun sharedYoutubeContractCorpus() {
        val source =
            javaClass.classLoader!!
                .getResourceAsStream("youtube-url-cases.json")!!
                .bufferedReader()
                .use { it.readText() }
        Json.decodeFromString<List<UrlCase>>(source).forEach {
            assertEquals(
                it.url,
                it.videoId?.let { id -> "https://www.youtube.com/watch?v=$id" },
                YoutubeLinks.canonical(it.url),
            )
        }
    }

    @Test
    fun rejectsAmbiguousAndEncodedYoutubePaths() {
        listOf(
                "https://youtu.be/dQw4w9WgXcQ/extra",
                "https://www.youtube.com/watch?v=dQw4w9WgXcQ&v=dQw4w9WgXcQ",
                "https://www.youtube.com/shorts/%64Qw4w9WgXcQ",
                "https://www.youtube.com./watch?v=dQw4w9WgXcQ",
                "https://youtu.be/dQw4w9WgXcQ%2F",
            )
            .forEach { assertNull(it, YoutubeLinks.canonical(it)) }
        assertEquals(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            YoutubeLinks.fromShare("A title\nhttps://youtu.be/dQw4w9WgXcQ?si=123"),
        )
    }

    @Test
    fun acceptsOnlyPrivateNumericAddresses() {
        listOf(
                "192.168.1.5",
                "10.1.2.3",
                "172.16.0.1",
                "172.31.255.254",
                "127.0.0.1",
                "::1",
                "fd12::1",
                "fe80::1",
            )
            .forEach { assertNotNull(LocalEndpoint.address(it)) }
        listOf(
                "8.8.8.8",
                "172.32.0.1",
                "169.254.1.2",
                "0.0.0.0",
                "224.1.1.1",
                "example.com",
                "192.168.001.2",
                "999.1.2.3",
                "2001:4860:4860::8888",
                "::",
            )
            .forEach { assertTrue(it, runCatching { LocalEndpoint.address(it) }.isFailure) }
    }

    @Test
    fun validatesFingerprintAndEndpoint() {
        val pin = "AB".repeat(32)
        assertEquals(
            pin.lowercase(),
            LocalEndpoint.normalizeFingerprint(pin.chunked(2).joinToString(":")),
        )
        assertTrue(runCatching { LocalEndpoint.normalizeFingerprint("ab") }.isFailure)
        assertTrue(runCatching { LocalEndpoint.baseUrl("10.0.0.2", 0) }.isFailure)
        assertEquals("https://[fd12::1]:8765", LocalEndpoint.baseUrl("fd12::1", 8765))
        val scoped = "fe80::1%1"
        val url = LocalEndpoint.baseUrl(scoped, 8765)
        assertNotNull(url.toHttpUrl())
        val client = RemoteTransport.pinnedClient(pin, scoped)
        try {
            val address = client.dns.lookup("bedside.invalid").single() as java.net.Inet6Address
            assertEquals(1, address.scopeId)
            assertEquals(java.net.Proxy.NO_PROXY, client.proxy)
        } finally {
            client.dispatcher.executorService.shutdown()
        }
    }

    @Test
    fun rejectsMalformedStateAndUnknownProtocol() {
        assertTrue(runCatching { RemoteState(version = 2).validate() }.isFailure)
        assertTrue(runCatching { RemoteState(activeSessionId = "missing").validate() }.isFailure)
        val playback =
            Playback(
                "youtube",
                1,
                title = "Video",
                paused = false,
                position = Double.NaN,
                duration = 1.0,
                ended = false,
                capabilities = listOf("seek"),
            )
        assertTrue(runCatching { RemoteState(playback = playback).validate() }.isFailure)
        assertTrue(
            runCatching {
                    RemoteState(
                            playback = playback.copy(position = 0.0, capabilities = listOf("shell"))
                        )
                        .validate()
                }
                .isFailure
        )
        assertEquals(
            RemoteState(),
            WireJson.decodeFromString<RemoteState>(
                    WireJson.encodeToString(RemoteState.serializer(), RemoteState())
                )
                .validate(),
        )
    }
}
