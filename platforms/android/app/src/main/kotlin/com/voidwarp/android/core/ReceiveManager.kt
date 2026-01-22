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
            else -> "$fileSize B"
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
        
        receiverHandle = NativeLib.voidwarpCreateReceiver()
        if (receiverHandle == 0L) {
            return false
        }
        
        _port.value = NativeLib.voidwarpReceiverGetPort(receiverHandle)
        return true
    }
    
    /**
     * Start listening for incoming transfers
     */
    fun startReceiving() {
        if (receiverHandle == 0L && !initialize()) {
            _state.value = ReceiverState.ERROR
            return
        }
        
        NativeLib.voidwarpReceiverStart(receiverHandle)
        _state.value = ReceiverState.LISTENING
        
        startPolling()
    }
    
    /**
     * Stop listening
     */
    fun stopReceiving() {
        pollingJob?.cancel()
        pollingJob = null
        
        if (receiverHandle != 0L) {
            NativeLib.voidwarpReceiverStop(receiverHandle)
        }
        
        _state.value = ReceiverState.IDLE
        _pendingTransfer.value = null
    }
    
    /**
     * Accept the pending transfer
     */
    suspend fun acceptTransfer(savePath: String): Boolean = withContext(Dispatchers.IO) {
        if (receiverHandle == 0L) return@withContext false
        
        _state.value = ReceiverState.RECEIVING
        
        val result = NativeLib.voidwarpReceiverAccept(receiverHandle, savePath)
        
        if (result == 0) {
            // Monitor progress during receive
            while (_state.value == ReceiverState.RECEIVING) {
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
            return@withContext _state.value == ReceiverState.COMPLETED
        }
        
        false
    }
    
    /**
     * Reject the pending transfer
     */
    fun rejectTransfer() {
        if (receiverHandle == 0L) return
        
        NativeLib.voidwarpReceiverReject(receiverHandle)
        _pendingTransfer.value = null
        _state.value = ReceiverState.LISTENING
        
        // Restart listening
        startReceiving()
    }
    
    private fun startPolling() {
        pollingJob?.cancel()
        pollingJob = scope.launch {
            while (isActive) {
                if (receiverHandle == 0L) break
                
                val nativeState = NativeLib.voidwarpReceiverGetState(receiverHandle)
                val newState = ReceiverState.fromInt(nativeState)
                
                if (newState != _state.value) {
                    _state.value = newState
                }
                
                // Check for pending transfer
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
                
                // Update progress if receiving
                if (newState == ReceiverState.RECEIVING) {
                    _progress.value = NativeLib.voidwarpReceiverGetProgress(receiverHandle)
                    _bytesReceived.value = NativeLib.voidwarpReceiverGetBytesReceived(receiverHandle)
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
            NativeLib.voidwarpDestroyReceiver(receiverHandle)
            receiverHandle = 0
        }
    }
}
