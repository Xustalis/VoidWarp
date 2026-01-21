package com.voidwarp.android.core

import com.voidwarp.android.native.NativeLib
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
    
    fun startDiscovery(port: Int = 42424): Boolean {
        val result = NativeLib.voidwarpStartDiscovery(handle, port)
        _isDiscovering.value = result == 0
        return _isDiscovering.value
    }
    
    fun stopDiscovery() {
        NativeLib.voidwarpStopDiscovery(handle)
        _isDiscovering.value = false
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
