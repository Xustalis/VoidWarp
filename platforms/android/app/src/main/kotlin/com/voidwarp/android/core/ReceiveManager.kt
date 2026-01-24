package com.voidwarp.android.core

import com.voidwarp.android.native.NativeLib
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Receiver state enum
 */
enum class ReceiverState {
    IDLE,
    LISTENING,
    AWAITING_ACCEPT,
    RECEIVING,
    COMPLETED,
    ERROR;
    
    companion object {
        fun fromInt(value: Int): ReceiverState = when (value) {
            0 -> IDLE
            1 -> LISTENING
            2 -> AWAITING_ACCEPT
            3 -> RECEIVING
            4 -> COMPLETED
            5 -> ERROR
            else -> ERROR
        }
    }
}

/**
 * Information about a pending incoming transfer
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
            else -> "$fileSize 字节"
        }
}

/**
 * Manages file receiving operations
 */
class ReceiveManager {
    
    private var receiverHandle: Long = 0
    private var pollingJob: Job? = null
    private val scope = CoroutineScope(Dispatchers.Default + SupervisorJob())
    
    private val _state = MutableStateFlow(ReceiverState.IDLE)
    val state: StateFlow<ReceiverState> = _state.asStateFlow()
    
    private val _port = MutableStateFlow(0)
    val port: StateFlow<Int> = _port.asStateFlow()
    
    private val _pendingTransfer = MutableStateFlow<PendingTransfer?>(null)
    val pendingTransfer: StateFlow<PendingTransfer?> = _pendingTransfer.asStateFlow()
    
    private val _progress = MutableStateFlow(0f)
    val progress: StateFlow<Float> = _progress.asStateFlow()
    
    private val _bytesReceived = MutableStateFlow(0L)
    val bytesReceived: StateFlow<Long> = _bytesReceived.asStateFlow()
    
    /**
     * Initialize the receiver
     */
    fun initialize(): Boolean {
        if (receiverHandle != 0L) return true
        
        return try {
            android.util.Log.d("ReceiveManager", "Creating receiver...")
            receiverHandle = NativeLib.voidwarpCreateReceiver()
            if (receiverHandle == 0L) {
                android.util.Log.e("ReceiveManager", "Failed to create receiver (null handle)")
                return false
            }
            
            _port.value = NativeLib.voidwarpReceiverGetPort(receiverHandle)
            if (_port.value <= 0) {
                NativeLib.voidwarpDestroyReceiver(receiverHandle)
                receiverHandle = 0
                return false
            }
            android.util.Log.d("ReceiveManager", "Receiver created on port: ${_port.value}")
            true
        } catch (t: Throwable) {
            android.util.Log.e("ReceiveManager", "Exception creating receiver: ${t.message}", t)
            false
        }
    }
    
    /**
     * Start listening for incoming transfers
     */
    fun startReceiving() {
        try {
            if (receiverHandle == 0L && !initialize()) {
                _state.value = ReceiverState.ERROR
                return
            }
            
            android.util.Log.d("ReceiveManager", "Starting receiver...")
            NativeLib.voidwarpReceiverStart(receiverHandle)
            val started = NativeLib.voidwarpTransportStartServer(_port.value)
            if (!started) {
                _state.value = ReceiverState.ERROR
                return
            }
            _state.value = ReceiverState.LISTENING
            
            startPolling()
        } catch (t: Throwable) {
            android.util.Log.e("ReceiveManager", "Exception starting receiver: ${t.message}", t)
            _state.value = ReceiverState.ERROR
        }
    }
    
    /**
     * Stop listening
     */
    fun stopReceiving() {
        pollingJob?.cancel()
        pollingJob = null
        
        if (receiverHandle != 0L) {
            try {
                NativeLib.voidwarpReceiverStop(receiverHandle)
            } catch (_: Throwable) {
            }
        }
        
        _state.value = ReceiverState.IDLE
        _pendingTransfer.value = null
    }
    
    /**
     * Accept the pending transfer
     */
    suspend fun acceptTransfer(savePath: String): Boolean = withContext(Dispatchers.IO) {
        if (receiverHandle == 0L) return@withContext false
        if (savePath.isBlank()) return@withContext false
        
        return@withContext try {
            _state.value = ReceiverState.RECEIVING
            
            val result = NativeLib.voidwarpReceiverAccept(receiverHandle, savePath)
            
            if (result == 0) {
                while (isActive && _state.value == ReceiverState.RECEIVING) {
                    _progress.value = NativeLib.voidwarpReceiverGetProgress(receiverHandle)
                    _bytesReceived.value = NativeLib.voidwarpReceiverGetBytesReceived(receiverHandle)
                    
                    val newState = ReceiverState.fromInt(
                        NativeLib.voidwarpReceiverGetState(receiverHandle)
                    )
                    
                    if (newState == ReceiverState.COMPLETED || newState == ReceiverState.ERROR) {
                        _state.value = newState
                        break
                    }
                    
                    delay(100)
                }
                
                _pendingTransfer.value = null
                _state.value == ReceiverState.COMPLETED
            } else {
                _state.value = ReceiverState.ERROR
                false
            }
        } catch (_: Throwable) {
            _state.value = ReceiverState.ERROR
            false
        }
    }
    
    /**
     * Reject the pending transfer
     */
    fun rejectTransfer() {
        if (receiverHandle == 0L) return
        
        try {
            NativeLib.voidwarpReceiverReject(receiverHandle)
            _pendingTransfer.value = null
            _state.value = ReceiverState.LISTENING
            startReceiving()
        } catch (_: Throwable) {
            _state.value = ReceiverState.ERROR
        }
    }
    
    private fun startPolling() {
        pollingJob?.cancel()
        pollingJob = scope.launch {
            while (isActive) {
                if (receiverHandle == 0L) break
                
                try {
                    val nativeState = NativeLib.voidwarpReceiverGetState(receiverHandle)
                    val newState = ReceiverState.fromInt(nativeState)
                    
                    if (newState != _state.value) {
                        _state.value = newState
                    }
                    
                    if (newState == ReceiverState.AWAITING_ACCEPT) {
                        val pending = NativeLib.voidwarpReceiverGetPending(receiverHandle)
                        if (pending != null && _pendingTransfer.value == null) {
                            _pendingTransfer.value = PendingTransfer(
                                senderName = pending.senderName,
                                senderAddress = pending.senderAddress,
                                fileName = pending.fileName,
                                fileSize = pending.fileSize
                            )
                        }
                    }
                    
                    if (newState == ReceiverState.RECEIVING) {
                        _progress.value = NativeLib.voidwarpReceiverGetProgress(receiverHandle)
                        _bytesReceived.value = NativeLib.voidwarpReceiverGetBytesReceived(receiverHandle)
                    }
                } catch (_: Throwable) {
                    _state.value = ReceiverState.ERROR
                    break
                }
                
                delay(200)
            }
        }
    }
    
    /**
     * Cleanup resources
     */
    fun close() {
        stopReceiving()
        scope.cancel()
        
        if (receiverHandle != 0L) {
            try {
                NativeLib.voidwarpDestroyReceiver(receiverHandle)
            } catch (_: Throwable) {
            } finally {
                receiverHandle = 0
            }
        }
    }
}
