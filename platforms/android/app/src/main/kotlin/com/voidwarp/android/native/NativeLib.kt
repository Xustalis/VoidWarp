package com.voidwarp.android.native

import android.util.Log

/**
 * JNI bindings to the VoidWarp Rust core library
 */
object NativeLib {
    
    private const val TAG = "VoidWarpNative"
    
    init {
        try {
            System.loadLibrary("voidwarp_core")
            Log.i(TAG, "Native library loaded successfully")
        } catch (e: UnsatisfiedLinkError) {
            Log.e(TAG, "Failed to load native library", e)
        }
    }
    
    // Handle management
    external fun voidwarpInit(deviceName: String): Long
    external fun voidwarpDestroy(handle: Long)
    
    // Device info
    external fun voidwarpGetDeviceId(handle: Long): String?
    external fun voidwarpGeneratePairingCode(): String?
    
    // Discovery
    external fun voidwarpStartDiscovery(handle: Long, port: Int): Int
    external fun voidwarpStopDiscovery(handle: Long)
    external fun voidwarpGetPeers(handle: Long): Array<PeerInfo>?
    
    // File transfer
    external fun voidwarpCreateSender(path: String): Long
    external fun voidwarpSenderGetSize(sender: Long): Long
    external fun voidwarpSenderGetName(sender: Long): String?
    external fun voidwarpSenderGetProgress(sender: Long): TransferProgress?
    external fun voidwarpSenderCancel(sender: Long)
    external fun voidwarpDestroySender(sender: Long)
    
    /**
     * Peer information from discovery
     */
    data class PeerInfo(
        val deviceId: String,
        val deviceName: String,
        val ipAddress: String,
        val port: Int
    )
    
    /**
     * Transfer progress information
     */
    data class TransferProgress(
        val bytesTransferred: Long,
        val totalBytes: Long,
        val percentage: Float,
        val speedMbps: Float,
        val state: Int
    ) {
        val isCompleted: Boolean get() = state == 3
        val isFailed: Boolean get() = state == 4
        val isCancelled: Boolean get() = state == 5
    }
}
