package com.bedside.remote

import android.content.Context
import android.net.nsd.NsdManager
import android.net.nsd.NsdServiceInfo
import android.os.Build
import java.net.NetworkInterface
import java.util.ArrayDeque

data class DiscoveredComputer(val name: String, val host: String, val port: Int)

/** Discovery is a convenience, never a source of certificate trust. */
@Suppress("DEPRECATION")
class ComputerDiscovery(
    context: Context,
    private val onDevices: (List<DiscoveredComputer>) -> Unit,
    private val onError: (String) -> Unit,
) {
    private val manager = context.getSystemService(NsdManager::class.java)
    private val devices = linkedMapOf<String, DiscoveredComputer>()
    private val resolveQueue = ArrayDeque<NsdServiceInfo>()
    private var resolving = false
    private var listener: NsdManager.DiscoveryListener? = null
    private var epoch = 0

    @Synchronized
    fun start() {
        if (listener != null) return
        val current = ++epoch
        val next =
            object : NsdManager.DiscoveryListener {
                override fun onDiscoveryStarted(type: String) = Unit

                override fun onDiscoveryStopped(type: String) = Unit

                override fun onStartDiscoveryFailed(type: String, errorCode: Int) {
                    onError("LAN discovery unavailable. Add the computer's IP address manually.")
                    stop()
                }

                override fun onStopDiscoveryFailed(type: String, errorCode: Int) = Unit

                override fun onServiceLost(serviceInfo: NsdServiceInfo) =
                    synchronized(this@ComputerDiscovery) {
                        devices.remove(serviceInfo.serviceName)
                        onDevices(devices.values.toList())
                    }

                override fun onServiceFound(serviceInfo: NsdServiceInfo) =
                    synchronized(this@ComputerDiscovery) {
                        if (
                            current == epoch &&
                                serviceInfo.serviceType.startsWith("_bedside-remote._tcp")
                        ) {
                            resolveQueue.add(serviceInfo)
                            resolve(current)
                        }
                    }
            }
        listener = next
        runCatching {
                manager.discoverServices("_bedside-remote._tcp.", NsdManager.PROTOCOL_DNS_SD, next)
            }
            .onFailure {
                listener = null
                onError("LAN discovery unavailable. Add a computer manually.")
            }
    }

    @Synchronized
    private fun resolve(current: Int) {
        if (resolving || resolveQueue.isEmpty() || current != epoch) return
        resolving = true
        manager.resolveService(
            resolveQueue.removeFirst(),
            object : NsdManager.ResolveListener {
                override fun onResolveFailed(info: NsdServiceInfo, code: Int) =
                    synchronized(this@ComputerDiscovery) {
                        resolving = false
                        resolve(current)
                    }

                override fun onServiceResolved(info: NsdServiceInfo) =
                    synchronized(this@ComputerDiscovery) {
                        if (current == epoch) {
                            val version = info.attributes["version"]?.toString(Charsets.UTF_8)
                            val resolved =
                                if (Build.VERSION.SDK_INT >= 34) info.hostAddresses
                                else listOfNotNull(info.host)
                            // Older NSD APIs expose only the first A record, which may be a VPN.
                            val hints =
                                info.attributes["addresses"]
                                    ?.takeIf { it.size <= 245 }
                                    ?.toString(Charsets.UTF_8)
                                    ?.split(',')
                                    ?.take(16)
                                    .orEmpty()
                            val subnets =
                                runCatching {
                                        NetworkInterface.getNetworkInterfaces()
                                            .toList()
                                            .filter { it.isUp && !it.isLoopback }
                                            .flatMap { it.interfaceAddresses }
                                            .map {
                                                DiscoverySubnet(
                                                    it.address,
                                                    it.networkPrefixLength.toInt(),
                                                )
                                            }
                                    }
                                    .getOrDefault(emptyList())
                            val host =
                                DiscoveryHost.select(
                                    resolved.mapNotNull { it.hostAddress } + hints,
                                    subnets,
                                )
                            if (version == "1" && host != null && info.port in 1..65535) {
                                devices[info.serviceName] =
                                    DiscoveredComputer(info.serviceName, host, info.port)
                                onDevices(devices.values.toList())
                            }
                        }
                        resolving = false
                        resolve(current)
                    }
            },
        )
    }

    @Synchronized
    fun stop() {
        ++epoch
        listener?.let { runCatching { manager.stopServiceDiscovery(it) } }
        listener = null
        resolveQueue.clear()
        resolving = false
        devices.clear()
        onDevices(emptyList())
    }
}
