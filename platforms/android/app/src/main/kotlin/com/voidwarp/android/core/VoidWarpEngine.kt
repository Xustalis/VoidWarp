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
    
    private var handle: Long = NativeLib.voidwarpInit(deviceName)
    
    val deviceId: String = NativeLib.voidwarpGetDeviceId(handle) ?: "unknown"
    
    private val _isDiscovering = MutableStateFlow(false)
    val isDiscovering: StateFlow<Boolean> = _isDiscovering.asStateFlow()
    
    private val _peers = MutableStateFlow<List<DiscoveredPeer>>(emptyList())
    val peers: StateFlow<List<DiscoveredPeer>> = _peers.asStateFlow()
    
    fun generatePairingCode(): String {
        return NativeLib.voidwarpGeneratePairingCode() ?: "000-000"
    }
    
    suspend fun startDiscovery(receiverPort: Int = 42424): Boolean = withContext(Dispatchers.IO) {
        try {
            android.util.Log.d("VoidWarpEngine", "Starting discovery with port: $receiverPort")
            
            // Call native discovery - now always returns 0 (success)
            val result = NativeLib.voidwarpStartDiscovery(handle, receiverPort)
            android.util.Log.d("VoidWarpEngine", "Native discovery returned: $result")
            
            // Discovery now always succeeds (Rust returns 0 even in fallback mode)
            // Set discovering state to true
            _isDiscovering.value = true

            // Always add USB localhost peer for ADB port forwarding scenarios
            // When connected via USB with `adb reverse tcp:X tcp:X`, Android can reach
            // Windows at 127.0.0.1:X
            try {
                android.util.Log.d("VoidWarpEngine", "Adding USB localhost peer (127.0.0.1:42424)")
                NativeLib.voidwarpAddManualPeer(
                    handle,
                    "usb-windows",
                    "USB/Windows (localhost)",
                    "127.0.0.1",
                    42424  // Standard port for ADB reverse forwarding
                )
            } catch (e: Throwable) {
                android.util.Log.w("VoidWarpEngine", "Failed to add USB peer: ${e.message}")
            }

            true
        } catch (t: Throwable) {
            android.util.Log.e("VoidWarpEngine", "Discovery failed with exception", t)
            // Even on exception, set discovery to true so manual peers can still be added
            _isDiscovering.value = true
            true // Return true anyway - we can still use manual peers
        }
    }
    
    fun addManualPeer(id: String, name: String, ip: String, port: Int): Boolean {
        return try {
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
            NativeLib.voidwarpStopDiscovery(handle)
        } catch (_: Throwable) {
            // ignore
        } finally {
            _isDiscovering.value = false
        }
    }
    
    fun refreshPeers() {
        try {
            val nativePeers = NativeLib.voidwarpGetPeers(handle) ?: emptyArray()
            _peers.value = nativePeers.map { 
                DiscoveredPeer(
                    deviceId = it.deviceId,
                    deviceName = it.deviceName,
                    ipAddress = it.ipAddress,
                    port = it.port
                )
            }
        } catch (t: Throwable) {
            android.util.Log.e("VoidWarpEngine", "Failed to refresh peers: ${t.message}", t)
        }
    }
    
    override fun close() {
        if (handle != 0L) {
            NativeLib.voidwarpDestroy(handle)
            handle = 0L
        }
    }
}

data class DiscoveredPeer(
    val deviceId: String,
    val deviceName: String,
    val ipAddress: String,
    val port: Int
) {
    val displayName: String get() = "$deviceName ($ipAddress)"
}
