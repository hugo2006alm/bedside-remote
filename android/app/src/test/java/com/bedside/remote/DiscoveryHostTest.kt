package com.bedside.remote

import java.net.InetAddress
import org.junit.Assert.*
import org.junit.Test

class DiscoveryHostTest {
    private fun subnet(address: String, prefix: Int) =
        DiscoverySubnet(InetAddress.getByName(address), prefix)

    @Test
    fun prefersWifiOverVpnAndVirtualAdapters() {
        val addresses = listOf("100.65.203.96", "192.168.56.1", "172.30.64.1", "192.168.1.28")
        assertEquals(
            "192.168.1.28",
            DiscoveryHost.select(addresses, listOf(subnet("192.168.1.141", 24))),
        )
        // Legacy NSD's single host plus the Windows TXT hints follow the same policy.
        assertEquals(
            "192.168.1.28",
            DiscoveryHost.select(
                listOf(addresses.first()) + addresses.drop(1),
                listOf(subnet("192.168.1.141", 24)),
            ),
        )
    }

    @Test
    fun handlesNonOctetPrefixesAndRejectsUnapprovedAddresses() {
        assertEquals(
            "10.2.3.130",
            DiscoveryHost.select(listOf("10.2.3.1", "10.2.3.130"), listOf(subnet("10.2.3.150", 25))),
        )
        assertNull(
            DiscoveryHost.select(
                listOf("100.65.203.96", "8.8.8.8", "127.0.0.1", "example.com", "bad"),
                emptyList(),
            )
        )
        assertEquals("10.1.2.3", DiscoveryHost.select(listOf("10.1.2.3"), emptyList()))
    }

    @Test
    fun supportsPrivateIpv6AndIgnoresInvalidPrefixes() {
        val expected = InetAddress.getByName("fd12::2").hostAddress
        assertEquals(
            expected,
            DiscoveryHost.select(listOf("192.168.56.1", "fd12::2"), listOf(subnet("fd12::1", 64))),
        )
        assertEquals(
            "192.168.1.28",
            DiscoveryHost.select(
                listOf("192.168.1.28"),
                listOf(subnet("10.0.0.1", 0), subnet("10.0.0.1", 33)),
            ),
        )
    }
}
