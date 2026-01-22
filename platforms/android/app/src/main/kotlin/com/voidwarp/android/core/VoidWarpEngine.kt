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
    
    suspend fun startDiscovery(port: Int = 42424): Boolean = withContext(Dispatchers.IO) {
        try {
            val result = NativeLib.voidwarpStartDiscovery(handle, port)
            _isDiscovering.value = result == 0

            // Auto-add localhost for USB/ADB forwarding scenarios
            // This ensures that if the user has set up 'adb forward', they can connect via localhost
            if (_isDiscovering.value) {
                addManualPeer("usb-host", "USB/Localhost", "127.0.0.1", port)
            }

            _isDiscovering.value
        } catch (t: Throwable) {
            // Never crash UI from JNI problems. Keep state consistent.
            _isDiscovering.value = false
            false
        }
    }
    
    fun addManualPeer(id: String, name: String, ip: String, port: Int) {
        NativeLib.voidwarpAddManualPeer(handle, id, name, ip, port)
        refreshPeers() // Force refresh to show the new peer
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
        val nativePeers = NativeLib.voidwarpGetPeers(handle) ?: emptyArray()
        _peers.value = nativePeers.map { 
            DiscoveredPeer(
                deviceId = it.deviceId,
                deviceName = it.deviceName,
                ipAddress = it.ipAddress,
                port = it.port
            )
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
