package com.bedside.remote

import java.security.MessageDigest
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import okhttp3.tls.HandshakeCertificates
import okhttp3.tls.HeldCertificate
import org.junit.Assert.*
import org.junit.Test

class TransportSecurityTest {
    @Test
    fun transientCloseDoesNotRevokeValidPairing() {
        for (code in listOf(1008, 1013)) {
            server().use { server ->
                val reconnecting = CountDownLatch(1)
                val statuses =
                    java.util.Collections.synchronizedList(mutableListOf<ConnectionStatus>())
                server.enqueue(
                    MockResponse()
                        .withWebSocketUpgrade(
                            object : WebSocketListener() {
                                override fun onOpen(webSocket: WebSocket, response: Response) {
                                    webSocket.close(code, "Retry later")
                                }

                                override fun onClosing(
                                    webSocket: WebSocket,
                                    code: Int,
                                    reason: String,
                                ) {
                                    webSocket.close(code, reason)
                                }
                            }
                        )
                )
                val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
                val transport =
                    RemoteTransport(
                        scope,
                        {
                            statuses.add(it)
                            if (it == ConnectionStatus.RECONNECTING) reconnecting.countDown()
                        },
                        {},
                        {},
                        {},
                    )
                try {
                    transport.connect(
                        PairedComputer(
                            "phone",
                            "PC",
                            "127.0.0.1",
                            server.port,
                            fingerprint,
                            "test-token",
                        )
                    )
                    assertTrue(reconnecting.await(5, TimeUnit.SECONDS))
                    assertFalse(statuses.contains(ConnectionStatus.REVOKED))
                } finally {
                    transport.disconnect()
                    scope.cancel()
                }
            }
        }
    }

    @Test
    fun endpointEditVerifiesPinWithoutSendingStoredToken() = runBlocking {
        server().use { server ->
            server.enqueue(MockResponse().setBody("""{"version":1,"name":"PC"}"""))
            val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
            val transport = RemoteTransport(scope, {}, {}, {}, {})
            try {
                val computer =
                    PairedComputer(
                        "phone",
                        "PC",
                        "127.0.0.1",
                        server.port,
                        fingerprint,
                        "private-token",
                    )
                transport.verifyEndpoint(computer)
                val request = server.takeRequest(5, TimeUnit.SECONDS)!!
                assertEquals("/health", request.path)
                assertNull(request.getHeader("Authorization"))
                assertTrue(
                    runCatching {
                            transport.verifyEndpoint(computer.copy(fingerprint = "00".repeat(32)))
                        }
                        .isFailure
                )
                assertEquals(1, server.requestCount)
            } finally {
                transport.disconnect()
                scope.cancel()
            }
        }
    }

    @Test
    fun rejectedAcknowledgementReportsFailedDraftWithoutAcceptance() {
        server().use { server ->
            val opened = CountDownLatch(1)
            val rejected = CountDownLatch(1)
            var accepted = false
            server.enqueue(
                MockResponse()
                    .withWebSocketUpgrade(
                        object : WebSocketListener() {
                            override fun onMessage(webSocket: WebSocket, text: String) {
                                val id =
                                    WireJson.parseToJsonElement(text)
                                        .jsonObject["id"]!!
                                        .jsonPrimitive
                                        .content
                                webSocket.send(
                                    """{"version":1,"type":"ack","id":"$id","ok":false,"error":"Busy"}"""
                                )
                            }

                            override fun onClosing(
                                webSocket: WebSocket,
                                code: Int,
                                reason: String,
                            ) {
                                webSocket.close(code, reason)
                            }
                        }
                    )
            )
            val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
            val transport =
                RemoteTransport(
                    scope,
                    { if (it == ConnectionStatus.CONNECTED) opened.countDown() },
                    {},
                    {},
                    { accepted = true },
                    { _, action, ok -> if (action == "input.text" && !ok) rejected.countDown() },
                )
            try {
                transport.connect(
                    PairedComputer(
                        "phone",
                        "PC",
                        "127.0.0.1",
                        server.port,
                        fingerprint,
                        "test-token",
                    )
                )
                assertTrue(opened.await(5, TimeUnit.SECONDS))
                transport.send(
                    "input.text",
                    JsonObject(mapOf("text" to JsonPrimitive("Keep this draft"))),
                )
                assertTrue(rejected.await(5, TimeUnit.SECONDS))
                assertFalse(accepted)
            } finally {
                transport.disconnect()
                scope.cancel()
            }
        }
    }

    private val certificate =
        HeldCertificate.Builder()
            .commonName("Bedside test")
            .addSubjectAlternativeName("127.0.0.1")
            .build()
    private val fingerprint =
        MessageDigest.getInstance("SHA-256").digest(certificate.certificate.encoded).joinToString(
            ""
        ) {
            "%02x".format(it.toInt() and 255)
        }

    private fun server() =
        MockWebServer().apply {
            useHttps(
                HandshakeCertificates.Builder()
                    .heldCertificate(certificate)
                    .build()
                    .sslSocketFactory(),
                false,
            )
            start()
        }

    @Test
    fun pinnedPairingSendsSecretOnlyToVerifiedComputer() = runBlocking {
        server().use { server ->
            server.enqueue(
                MockResponse()
                    .setBody(
                        """{"version":1,"deviceId":"phone","token":"${"a".repeat(64)}","deviceName":"Bedroom PC"}"""
                    )
            )
            val paired =
                RemoteTransport.pair("127.0.0.1", server.port, fingerprint, "123456", "Test phone")
            assertEquals("Bedroom PC", paired.name)
            assertEquals(fingerprint, paired.fingerprint)
            val request = server.takeRequest(5, TimeUnit.SECONDS)!!
            assertEquals("/pair", request.path)
            assertTrue(request.body.readUtf8().contains("123456"))
            assertNull(request.getHeader("Authorization"))
        }
    }

    @Test
    fun mismatchedPinNeverSendsPairingSecret() = runBlocking {
        server().use { server ->
            val result = runCatching {
                RemoteTransport.pair(
                    "127.0.0.1",
                    server.port,
                    "00".repeat(32),
                    "123456",
                    "Test phone",
                )
            }
            assertTrue(result.isFailure)
            assertEquals(0, server.requestCount)
        }
    }

    @Test
    fun websocketAuthenticatesAndSequencesCommands() {
        server().use { server ->
            val opened = CountDownLatch(1)
            val received = CountDownLatch(2)
            val accepted = CountDownLatch(2)
            val commands = java.util.Collections.synchronizedList(mutableListOf<String>())
            server.enqueue(
                MockResponse()
                    .withWebSocketUpgrade(
                        object : WebSocketListener() {
                            override fun onOpen(webSocket: WebSocket, response: Response) {
                                webSocket.send(
                                    WireJson.encodeToString(RemoteState.serializer(), RemoteState())
                                )
                            }

                            override fun onClosing(
                                webSocket: WebSocket,
                                code: Int,
                                reason: String,
                            ) {
                                webSocket.close(code, reason)
                            }

                            override fun onMessage(webSocket: WebSocket, text: String) {
                                commands.add(text)
                                val id =
                                    WireJson.parseToJsonElement(text)
                                        .jsonObject["id"]!!
                                        .jsonPrimitive
                                        .content
                                webSocket.send(
                                    """{"version":1,"type":"ack","id":"$id","ok":true}"""
                                )
                                received.countDown()
                            }
                        }
                    )
            )
            val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
            val transport =
                RemoteTransport(
                    scope,
                    { if (it == ConnectionStatus.CONNECTED) opened.countDown() },
                    {},
                    {},
                    { accepted.countDown() },
                )
            try {
                transport.connect(
                    PairedComputer(
                        "phone",
                        "PC",
                        "127.0.0.1",
                        server.port,
                        fingerprint,
                        "token-for-loopback-test",
                    )
                )
                assertTrue(opened.await(5, TimeUnit.SECONDS))
                transport.send("input.key", JsonObject(mapOf("key" to JsonPrimitive("up"))))
                transport.send("input.key", JsonObject(mapOf("key" to JsonPrimitive("enter"))))
                assertTrue(received.await(5, TimeUnit.SECONDS))
                assertTrue(accepted.await(5, TimeUnit.SECONDS))
                val request = server.takeRequest(5, TimeUnit.SECONDS)!!
                assertEquals("Bearer token-for-loopback-test", request.getHeader("Authorization"))
                assertEquals("/ws", request.path)
                assertEquals(
                    listOf("1", "2"),
                    commands.map {
                        WireJson.parseToJsonElement(it)
                            .jsonObject["sequence"]!!
                            .jsonPrimitive
                            .content
                    },
                )
                transport.disconnect()
                assertNull(
                    transport.send("input.key", JsonObject(mapOf("key" to JsonPrimitive("up"))))
                )
            } finally {
                transport.disconnect()
                scope.cancel()
            }
        }
    }
}
