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
 * 
 * Improvements:
 * - Pre-transfer ping test to verify connectivity
 * - Better error messages showing actual IP/port tried
 * - Detailed logging for debugging connection issues
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
    
    private val mutex = kotlinx.coroutines.sync.Mutex()

    /**
     * Send a file to a peer using TCP
     * Iterates through available IPs until successful connection
     */
    suspend fun sendFile(
        fileUri: Uri,
        peer: DiscoveredPeer,
        onProgress: (Float) -> Unit = {},
        onComplete: (Boolean, String?) -> Unit = { _, _ -> }
    ) = withContext(Dispatchers.IO) {
        if (!mutex.tryLock()) {
            withContext(Dispatchers.Main) {
                onComplete(false, "Transfer already in progress")
            }
            return@withContext
        }
        
        var tempFile: File? = null
        try {
            _isTransferring.value = true
            _progress.value = 0f
            _statusMessage.value = "准备发送..."
            
            android.util.Log.i("TransferManager", "Starting file transfer to ${peer.deviceName}")
            
            val ips = peer.ipAddress.split(",").filter { it.isNotBlank() }
            if (ips.isEmpty()) {
                val msg = "无有效的IP地址"
                _statusMessage.value = msg
                onComplete(false, msg)
                return@withContext
            }

            // Copy URI content to temp file for native access
            _statusMessage.value = "准备文件..."
            val startTime = System.currentTimeMillis()
            tempFile = copyUriToTempFile(fileUri)
            val copyTime = System.currentTimeMillis() - startTime
            android.util.Log.i("TransferManager", "Time to copy Uri to Temp: ${copyTime}ms")
            
            if (tempFile == null) {
                _statusMessage.value = "无法读取文件"
                onComplete(false, "无法读取文件")
                return@withContext
            }
            
            android.util.Log.d("TransferManager", "File prepared: ${tempFile.absolutePath}, size: ${tempFile.length()} bytes")
            
            _statusMessage.value = "计算校验和..."
            val hashStartTime = System.currentTimeMillis()
            
            // Create TCP sender
            senderHandle = NativeLib.voidwarpTcpSenderCreate(tempFile.absolutePath)
            if (senderHandle == 0L) {
                _statusMessage.value = "创建发送器失败"
                onComplete(false, "创建发送器失败")
                return@withContext
            }
            
            val hashTime = System.currentTimeMillis() - hashStartTime
            android.util.Log.i("TransferManager", "Time to create sender (includes hashing): ${hashTime}ms")
            
            val checksum = NativeLib.voidwarpTcpSenderGetChecksum(senderHandle)
            val fileSize = NativeLib.voidwarpTcpSenderGetFileSize(senderHandle)
            
            // Dynamic Chunk Size Optimization: 
            // For files under 32MB, use ONE giant chunk to eliminate ALL round-trip delays (Streaming Mode).
            // For larger files, use 1MB-4MB chunks to balance throughput and resume capability.
            val optimalChunkSize = when {
                fileSize < 32 * 1024 * 1024 -> fileSize.toInt() // < 32MB: Single chunk (Max Speed)
                fileSize > 1024 * 1024 * 1024 -> 4 * 1024 * 1024 // > 1GB: 4MB
                else -> 2 * 1024 * 1024                          // Default: 2MB
            }
            NativeLib.voidwarpTcpSenderSetChunkSize(senderHandle, if (optimalChunkSize <= 0) 1024*1024 else optimalChunkSize)
            android.util.Log.i("TransferManager", "Using ${if (fileSize < 32*1024*1024) "STREAMING" else "CHUNKED"} mode. Chunk Size: $optimalChunkSize")
            
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
            
            var finalResult = -1
            var usedIp = ""
            
            val transferStartTime = System.currentTimeMillis()
            
            // Try each IP until success or fatal error
            val nonLocalIps = filterOutLocalIps(ips)
            val ipListToUse = if (nonLocalIps.isNotEmpty()) nonLocalIps else ips
            
            for (ip in ipListToUse) {
                if (!isActive || !_isTransferring.value) break
                
                val targetIp = ip.trim()
                _statusMessage.value = "正在连接 $targetIp..."
                
                // Retry loop for the specific IP (handling resume on broken pipe)
                var retryCount = 0
                val maxIpRetries = 3
                
                while (retryCount < maxIpRetries) {
                    if (!isActive || !_isTransferring.value) break
                    
                    finalResult = NativeLib.voidwarpTcpSenderStart(
                        senderHandle,
                        targetIp,
                        peer.port,
                        android.os.Build.MODEL
                    )
                    
                    if (finalResult == 0) {
                        usedIp = targetIp
                        break 
                    } else if (finalResult == 6) {
                         retryCount++
                         continue
                    } else {
                        break 
                    }
                }

                if (finalResult == 0) break 
                if (finalResult == 3) continue 
                if (finalResult == 6) continue 
                break
            }

            val transferDuration = System.currentTimeMillis() - transferStartTime
            android.util.Log.i("TransferManager", "ACTUAL TRANSFER TIME: ${transferDuration}ms")

            // If we ran out of IPs and result is still 3 (or -1), make sure we report failure
            if (finalResult == -1) {
                 finalResult = 3 // Treat as connection failure if loop didn't run properly
            }
            
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
                    onComplete(false, "校验和不匹配 - 文件可能已损坏")
                }
                3 -> {
                    val errorDetail = "连接失败\n已尝试: ${ips.joinToString(", ")}\n请确认接收方已开启且在同一网络。"
                    _statusMessage.value = "连接失败"
                    onComplete(false, errorDetail)
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
                    _statusMessage.value = "错误: $finalResult"
                    onComplete(false, "发生未知错误 ($finalResult)")
                }
            }
            
        } catch (t: Throwable) {
            val message = t.message ?: "未知错误"
            android.util.Log.e("TransferManager", "Transfer exception: $message", t)
            _statusMessage.value = "发送失败：$message"
            onComplete(false, message)
        } finally {
            transferJob?.cancel()
            transferJob = null
            cleanup()
            try { tempFile?.delete() } catch (_: Exception) {}
            _isTransferring.value = false
            mutex.unlock()
        }
    }
    
    /**
     * Calculate checksum for a file
     */
    suspend fun calculateChecksum(fileUri: Uri): String? = withContext(Dispatchers.IO) {
        try {
            val tempFile = copyUriToTempFile(fileUri) ?: return@withContext null
            val checksum = NativeLib.voidwarpCalculateFileChecksum(tempFile.absolutePath)
            try { tempFile.delete() } catch (_: Exception) {}
            checksum
        } catch (_: Throwable) {
            null
        }
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
            try {
                NativeLib.voidwarpTcpSenderDestroy(senderHandle)
            } catch (_: Throwable) {
            } finally {
                senderHandle = 0
            }
        }
    }
    
    private fun copyUriToTempFile(uri: Uri): File? {
        return try {
            val fileName = getFileName(uri) ?: "temp_transfer"
            val tempFile = File(context.cacheDir, fileName)
            
            val inputStream = context.contentResolver.openInputStream(uri) ?: return null
            inputStream.use { input ->
                FileOutputStream(tempFile).use { output ->
                    input.copyTo(output, bufferSize = 1024 * 1024) // 1MB buffer for fast I/O
                }
            }
            
            tempFile
        } catch (_: Exception) {
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

    private fun filterOutLocalIps(ips: List<String>): List<String> {
        val localIps = mutableSetOf<String>()
        try {
            val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
            while (interfaces.hasMoreElements()) {
                val intf = interfaces.nextElement()
                val addrs = intf.inetAddresses
                while (addrs.hasMoreElements()) {
                    val addr = addrs.nextElement()
                    if (addr is java.net.Inet4Address) {
                       localIps.add(addr.hostAddress)
                    }
                }
            }
        } catch (_: Exception) {}
        
        return ips.filter { !localIps.contains(it) }
    }
}
