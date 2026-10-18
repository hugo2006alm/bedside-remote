package com.bedside.remote

import java.net.InetAddress

internal data class DiscoverySubnet(val address: InetAddress, val prefixLength: Int)

/** Discovery hints are untrusted; selecting a route never replaces certificate verification. */
internal object DiscoveryHost {
    fun select(candidates: List<String>, subnets: List<DiscoverySubnet>): String? =
        candidates
            .take(32)
            .mapNotNull { runCatching { LocalEndpoint.address(it) }.getOrNull() }
            .filterNot { it.isLoopbackAddress }
            .distinct()
            .maxByOrNull { candidate ->
                subnets.maxOfOrNull { subnet -> matchingPrefix(candidate, subnet) } ?: -1
            }
            ?.hostAddress

    private fun matchingPrefix(candidate: InetAddress, subnet: DiscoverySubnet): Int {
        val host = candidate.address
        val local = subnet.address.address
        val prefix = subnet.prefixLength
        if (host.size != local.size || prefix !in 1..(host.size * 8)) return -1
        for (bit in 0 until prefix) {
            val mask = 1 shl (7 - bit % 8)
            if ((host[bit / 8].toInt() and mask) != (local[bit / 8].toInt() and mask)) return -1
        }
        return prefix
    }
}
