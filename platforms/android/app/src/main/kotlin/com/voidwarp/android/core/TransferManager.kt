package com.voidwarp.android.core

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import com.voidwarp.android.native.NativeLib
import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.File
import java.io.FileOutputStream

/**
 * Manages file transfer operations (sending)
 */
class TransferManager(private val context: Context) {
    
    private var senderHandle: Long = 0
    private var transferJob: Job? = null
    
    private val _progress = MutableStateFlow(0f)
    val progress: StateFlow<Float> = _progress.asStateFlow()
    
    private val _isTransferring = MutableStateFlow(false)
    val isTransferring: StateFlow<Boolean> = _isTransferring.asStateFlow()
    
    private val _statusMessage = MutableStateFlow("等待传输...")
    val statusMessage: StateFlow<String> = _statusMessage.asStateFlow()
    
    /**
     * Send a file to a peer using TCP
     */
    suspend fun sendFile(
        fileUri: Uri,
        peer: DiscoveredPeer,
        onProgress: (Float) -> Unit = {},
        onComplete: (Boolean, String?) -> Unit = { _, _ -> }
    ) = withContext(Dispatchers.IO) {
        var tempFile: File? = null
        try {
            _isTransferring.value = true
            _progress.value = 0f
            _statusMessage.value = "准备发送..."
            
            // Copy URI content to temp file for native access
            tempFile = copyUriToTempFile(fileUri)
            if (tempFile == null) {
                _statusMessage.value = "无法读取文件"
                onComplete(false, "无法读取文件")
                return@withContext
            }
            
            _statusMessage.value = "计算校验和..."
            
            // Create TCP sender
            senderHandle = NativeLib.voidwarpTcpSenderCreate(tempFile.absolutePath)
            if (senderHandle == 0L) {
                _statusMessage.value = "创建发送器失败"
                onComplete(false, "创建发送器失败")
                return@withContext
            }
            
            NativeLib.voidwarpTcpSenderGetChecksum(senderHandle)
            NativeLib.voidwarpTcpSenderGetFileSize(senderHandle)
            
            _statusMessage.value = "连接 ${peer.deviceName}..."
            
            // Monitor progress
            transferJob = launch {
                while (isActive && _isTransferring.value) {
                    val prog = NativeLib.voidwarpTcpSenderGetProgress(senderHandle)
                    _progress.value = prog
                    onProgress(prog)
                    
                    if (prog >= 100f) {
                        break
                    }
                    
                    delay(200)
                }
            }
            
            // Start transfer
            _statusMessage.value = "正在发送..."
            
            // Try all available IPs (Smart Retry)
            val ips = peer.ipAddress.split(",").filter { it.isNotBlank() }
            var finalResult = 3 // Default to connection failed
            for (ip in ips) {
                if (!isActive || !_isTransferring.value) break // Stop if cancelled
                
                val targetIp = ip.trim()
                _statusMessage.value = "尝试连接 $targetIp..."
                android.util.Log.d("TransferManager", "Trying to send to $targetIp:${peer.port}")
                
                val result = NativeLib.voidwarpTcpSenderStart(
                    senderHandle,
                    targetIp,
                    peer.port,
                    android.os.Build.MODEL
                )
                
                if (result == 0) {
                    android.util.Log.i("TransferManager", "Success connected to $targetIp")
                    finalResult = 0
                    break // Success!
                } else if (result == 3) {
                    // Connection failed, try next IP
                    android.util.Log.w("TransferManager", "Connection to $targetIp failed (Result=3), trying next...")
                    continue
                } else {
                    // Other errors (rejected, checksum, etc.) are fatal for this transfer attempt
                    android.util.Log.e("TransferManager", "Fatal error during transfer to $targetIp: $result")
                    finalResult = result
                    break
                }
            }
            
            // Wait for progress job
            transferJob?.cancel()
            
            when (finalResult) {
                0 -> {
                    _statusMessage.value = "发送完成！"
                    _progress.value = 100f
                    onComplete(true, null)
                }
                1 -> {
                    _statusMessage.value = "对方拒绝了传输"
                    onComplete(false, "对方拒绝了传输")
                }
                2 -> {
                    _statusMessage.value = "校验和不匹配"
                    onComplete(false, "校验和不匹配")
                }
                3 -> {
                    _statusMessage.value = "连接失败 (尝试了所有IP)"
                    onComplete(false, "连接失败: 无法连接到设备 (${ips.size} IPs tried)")
                }
                4 -> {
                    _statusMessage.value = "传输超时"
                    onComplete(false, "传输超时")
                }
                5 -> {
                    _statusMessage.value = "已取消"
                    onComplete(false, "已取消")
                }
                else -> {
                    _statusMessage.value = "发生未知错误 ($finalResult)"
                    onComplete(false, "未知错误: $finalResult")
                }
            }
            
        } catch (e: Exception) {
            _statusMessage.value = "发送失败: ${e.message}"
            onComplete(false, e.message)
        } finally {
            cleanup()
            try { tempFile?.delete() } catch (_: Exception) {}
            _isTransferring.value = false
        }
    }
    
    /**
     * Calculate checksum for a file
     */
    suspend fun calculateChecksum(fileUri: Uri): String? = withContext(Dispatchers.IO) {
        val tempFile = copyUriToTempFile(fileUri) ?: return@withContext null
        val checksum = NativeLib.voidwarpCalculateFileChecksum(tempFile.absolutePath)
        tempFile.delete()
        checksum
    }
    
    /**
     * Cancel current transfer
     */
    fun cancel() {
        if (senderHandle != 0L) {
            NativeLib.voidwarpTcpSenderCancel(senderHandle)
        }
        transferJob?.cancel()
        _isTransferring.value = false
        _statusMessage.value = "已取消"
    }
    
    private fun cleanup() {
        if (senderHandle != 0L) {
            NativeLib.voidwarpTcpSenderDestroy(senderHandle)
            senderHandle = 0
        }
    }
    
    private fun copyUriToTempFile(uri: Uri): File? {
        return try {
            val fileName = getFileName(uri) ?: "temp_transfer"
            val tempFile = File(context.cacheDir, fileName)
            
            context.contentResolver.openInputStream(uri)?.use { input ->
                FileOutputStream(tempFile).use { output ->
                    input.copyTo(output, bufferSize = 1024 * 1024) // 1MB buffer
                }
            }
            
            tempFile
        } catch (e: Exception) {
            null
        }
    }
    
    private fun getFileName(uri: Uri): String? {
        return context.contentResolver.query(uri, null, null, null, null)?.use { cursor ->
            if (cursor.moveToFirst()) {
                val nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                if (nameIndex >= 0) cursor.getString(nameIndex) else null
            } else null
        }
    }
}
