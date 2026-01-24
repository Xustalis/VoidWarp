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
    
    // ========================================================================
    // Handle Management
    // ========================================================================
    
    external fun voidwarpInit(deviceName: String): Long
    external fun voidwarpDestroy(handle: Long)
    
    // ========================================================================
    // Device Info
    // ========================================================================
    
    external fun voidwarpGetDeviceId(handle: Long): String?
    external fun voidwarpGeneratePairingCode(): String?
    
    // ========================================================================
    // Discovery
    // ========================================================================
    
    external fun voidwarpStartDiscovery(handle: Long, ipAddress: String, port: Int): Int
    external fun voidwarpStopDiscovery(handle: Long)
    external fun voidwarpAddManualPeer(handle: Long, deviceId: String, deviceName: String, ipAddress: String, port: Int): Int
    external fun voidwarpGetPeers(handle: Long): Array<PeerInfo>?
    
    // ========================================================================
    // File Sender (legacy chunk-based)
    // ========================================================================
    
    external fun voidwarpCreateSender(path: String): Long
    external fun voidwarpSenderGetSize(sender: Long): Long
    external fun voidwarpSenderGetName(sender: Long): String?
    external fun voidwarpSenderGetProgress(sender: Long): TransferProgress?
    external fun voidwarpSenderCancel(sender: Long)
    external fun voidwarpDestroySender(sender: Long)
    
    // ========================================================================
    // File Receiver (TCP-based)
    // ========================================================================
    
    external fun voidwarpCreateReceiver(): Long
    external fun voidwarpReceiverGetPort(receiver: Long): Int
    external fun voidwarpReceiverStart(receiver: Long)
    external fun voidwarpReceiverStop(receiver: Long)
    external fun voidwarpReceiverGetState(receiver: Long): Int
    external fun voidwarpReceiverGetPending(receiver: Long): PendingTransfer?
    external fun voidwarpReceiverAccept(receiver: Long, savePath: String): Int
    external fun voidwarpReceiverReject(receiver: Long): Int
    external fun voidwarpReceiverGetProgress(receiver: Long): Float
    external fun voidwarpReceiverGetBytesReceived(receiver: Long): Long
    external fun voidwarpDestroyReceiver(receiver: Long)
    
    // ========================================================================
    // Checksum
    // ========================================================================
    
    external fun voidwarpCalculateFileChecksum(filePath: String): String?
    external fun voidwarpVerifyFileChecksum(filePath: String, expectedChecksum: String): Int
    
    // ========================================================================
    // TCP Sender (with checksums and resume)
    // ========================================================================
    
    external fun voidwarpTcpSenderCreate(filePath: String): Long
    external fun voidwarpTcpSenderStart(sender: Long, ipAddress: String, port: Int, senderName: String): Int
    external fun voidwarpTcpSenderGetChecksum(sender: Long): String?
    external fun voidwarpTcpSenderGetFileSize(sender: Long): Long
    external fun voidwarpTcpSenderGetProgress(sender: Long): Float
    external fun voidwarpTcpSenderCancel(sender: Long)
    external fun voidwarpTcpSenderDestroy(sender: Long)
    external fun voidwarpTcpSenderTestLink(ipAddress: String, port: Int): Int

    external fun voidwarpTransportStartServer(port: Int): Boolean
    external fun voidwarpTransportPing(ipAddress: String, port: Int): Boolean
    
    // ========================================================================
    // Data Classes
    // ========================================================================
    
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
    
    /**
     * Pending incoming transfer
     */
    data class PendingTransfer(
        val senderName: String,
        val senderAddress: String,
        val fileName: String,
        val fileSize: Long
    ) {
        val formattedSize: String
            get() = when {
                fileSize >= 1024 * 1024 * 1024 -> "%.2f GB".format(fileSize / 1024.0 / 1024.0 / 1024.0)
                fileSize >= 1024 * 1024 -> "%.1f MB".format(fileSize / 1024.0 / 1024.0)
                fileSize >= 1024 -> "%.1f KB".format(fileSize / 1024.0)
                else -> "$fileSize B"
            }
    }
    
    /**
     * Receiver state enum
     */
    object ReceiverState {
        const val IDLE = 0
        const val LISTENING = 1
        const val AWAITING_ACCEPT = 2
        const val RECEIVING = 3
        const val COMPLETED = 4
        const val ERROR = 5
    }
}
