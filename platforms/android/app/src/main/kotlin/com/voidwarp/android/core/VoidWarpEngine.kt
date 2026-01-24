package com.voidwarp.android.core

import com.voidwarp.android.native.NativeLib
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * High-level Kotlin wrapper for VoidWarp engine
 */
class VoidWarpEngine(deviceName: String) : AutoCloseable {
    
    private var handle: Long = try {
        NativeLib.voidwarpInit(deviceName)
    } catch (_: Throwable) {
        0L
    }
    
    val deviceId: String = try {
        if (handle != 0L) NativeLib.voidwarpGetDeviceId(handle) ?: "未知" else "未知"
    } catch (_: Throwable) {
        "未知"
    }
    
    private val _isDiscovering = MutableStateFlow(false)
    val isDiscovering: StateFlow<Boolean> = _isDiscovering.asStateFlow()
    
    private val _peers = MutableStateFlow<List<DiscoveredPeer>>(emptyList())
    val peers: StateFlow<List<DiscoveredPeer>> = _peers.asStateFlow()
    
    fun generatePairingCode(): String {
        return try {
            NativeLib.voidwarpGeneratePairingCode() ?: "000-000"
        } catch (_: Throwable) {
            "000-000"
        }
    }
    
    suspend fun startDiscovery(receiverPort: Int = 42424): Boolean = withContext(Dispatchers.IO) {
        try {
            if (handle == 0L) {
                _isDiscovering.value = true
                return@withContext true
            }
            android.util.Log.d("VoidWarpEngine", "Starting discovery with port: $receiverPort")
            
            // Get valid IP address for mDNS
            val ipAddress = getWifiIpAddress()
            android.util.Log.i("VoidWarpEngine", "Detected WiFi IP: $ipAddress")
            
            // Call native discovery with explicit IP
            val result = NativeLib.voidwarpStartDiscovery(handle, ipAddress, receiverPort)
            android.util.Log.d("VoidWarpEngine", "Native discovery returned: $result")
            
            // Discovery now always succeeds (Rust returns 0 even in fallback mode)
            // Set discovering state to true
            _isDiscovering.value = true

            true
        } catch (t: Throwable) {
            android.util.Log.e("VoidWarpEngine", "Discovery failed with exception", t)
            // Even on exception, set discovery to true so manual peers can still be added
            _isDiscovering.value = true
            true // Return true anyway - we can still use manual peers
        }
    }
    
    private fun getWifiIpAddress(): String {
        try {
             val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
             while (interfaces.hasMoreElements()) {
                 val networkInterface = interfaces.nextElement()
                 // Skip loopback and down interfaces
                 if (networkInterface.isLoopback || !networkInterface.isUp) continue
                 
                 // Check for "wlan" (WiFi), "ap" (Access Point/Hotspot), "eth" (Ethernet)
                 val name = networkInterface.name.lowercase()
                 val isPrioritized = name.contains("wlan") || name.contains("ap") || name.contains("eth") || name.contains("rndis") // USB Tethering
                 
                 // If not a prioritized interface, we might still use it if it has a valid local IP (192.168...)
                 
                 val addresses = networkInterface.inetAddresses
                 while (addresses.hasMoreElements()) {
                     val addr = addresses.nextElement()
                     
                     // We only want IPv4 for now
                     if (!addr.isLoopbackAddress && addr is java.net.Inet4Address) {
                         val ip = addr.hostAddress ?: continue
                         
                         // Prioritize standard local prefixes
                         if (ip.startsWith("192.168.") || ip.startsWith("10.") || ip.startsWith("172.")) {
                             if (isPrioritized) return ip // Perfect match
                         }
                         
                         // If we didn't return yet, keep searching for a better one, 
                         // but effectively we might return the first valid IPv4 if loop finishes.
                         // For simplicity, let's return the first prioritized one we found.
                         return ip
                     }
                 }
             }
        } catch (e: Exception) {
            android.util.Log.w("VoidWarpEngine", "Failed to get IP address: ${e.message}")
        }
        return "127.0.0.1"
    }
    
    fun addManualPeer(id: String, name: String, ip: String, port: Int): Boolean {
        return try {
            if (handle == 0L) return false
            android.util.Log.d("VoidWarpEngine", "Adding manual peer: $name at $ip:$port")
            NativeLib.voidwarpAddManualPeer(handle, id, name, ip, port)
            refreshPeers() // Force refresh to show the new peer
            android.util.Log.d("VoidWarpEngine", "Manual peer added successfully")
            true
        } catch (t: Throwable) {
            android.util.Log.e("VoidWarpEngine", "Failed to add manual peer: ${t.message}", t)
            false
        }
    }
    
    fun stopDiscovery() {
        try {
            if (handle != 0L) {
                NativeLib.voidwarpStopDiscovery(handle)
            }
        } catch (_: Throwable) {
            // ignore
        } finally {
            _isDiscovering.value = false
        }
    }
    
    fun refreshPeers() {
        try {
            if (handle == 0L) {
                _peers.value = emptyList()
                return
            }
            val nativePeers = NativeLib.voidwarpGetPeers(handle) ?: emptyArray()
            _peers.value = nativePeers.map { 
                DiscoveredPeer(
                    deviceId = it.deviceId,
                    deviceName = it.deviceName,
                    ipAddress = it.ipAddress, // This now contains "ip1,ip2,..."
                    port = it.port
                )
            }
        } catch (t: Throwable) {
            android.util.Log.e("VoidWarpEngine", "Failed to refresh peers: ${t.message}", t)
        }
    }
    
    suspend fun testConnection(peer: DiscoveredPeer): Boolean = withContext(Dispatchers.IO) {
        try {
            // Try all IPs
            val ips = peer.ipAddress.split(",").filter { it.isNotBlank() }
            for (ip in ips) {
                android.util.Log.d("VoidWarpEngine", "Testing connection to $ip:${peer.port}")
                val result = NativeLib.voidwarpTransportPing(ip.trim(), peer.port)
                if (result) {
                    android.util.Log.d("VoidWarpEngine", "Connection test passed for $ip")
                    return@withContext true
                }
            }
            android.util.Log.w("VoidWarpEngine", "Connection test failed for all IPs of ${peer.deviceName}")
            false
        } catch (t: Throwable) {
            android.util.Log.e("VoidWarpEngine", "Connection test error", t)
            false
        }
    }

    override fun close() {
        if (handle != 0L) {
            try {
                NativeLib.voidwarpDestroy(handle)
            } catch (_: Throwable) {
            } finally {
                handle = 0L
            }
        }
    }
}

data class DiscoveredPeer(
    val deviceId: String,
    val deviceName: String,
    val ipAddress: String, // Can be comma-separated list
    val port: Int
) {
    val displayName: String get() {
        // Show first IP or "Multiple IPs"
        val ips = ipAddress.split(",")
        return if (ips.size > 1) {
            "$deviceName (${ips[0]}...)"
        } else {
            "$deviceName ($ipAddress)"
        }
    }

    // Heuristics to get the "best" IP for display/usage if needed (though engine now handles multi-IP)
    val bestIp: String get() = ipAddress.split(",").firstOrNull()?.trim() ?: ""
}
