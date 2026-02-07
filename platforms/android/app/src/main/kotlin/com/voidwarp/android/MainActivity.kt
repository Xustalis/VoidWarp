package com.voidwarp.android

import android.Manifest
import android.content.pm.PackageManager
import android.net.Uri
import android.net.wifi.WifiManager
import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.widget.Toast
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import com.voidwarp.android.core.HistoryManager
import com.voidwarp.android.core.HistoryItem
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import android.media.MediaScannerConnection
import android.os.Environment
import java.io.File
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.animation.core.*
import androidx.compose.ui.draw.scale
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material.icons.filled.Link
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.CompletableDeferred
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.ui.unit.sp
import com.voidwarp.android.core.*
import com.voidwarp.android.ui.theme.VoidWarpTheme
import com.voidwarp.android.ui.ReceivedFilesScreen
import kotlinx.coroutines.delay


class MainActivity : ComponentActivity() {
    
    private var engine: VoidWarpEngine? = null
    private var transferManager: TransferManager? = null
    private var receiveManager: ReceiveManager? = null
    private var multicastLock: WifiManager.MulticastLock? = null
    

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // Android 13+ requires NEARBY_WIFI_DEVICES for Wi‑Fi LAN discovery.
        if (android.os.Build.VERSION.SDK_INT >= 33) {
            if (checkSelfPermission(Manifest.permission.NEARBY_WIFI_DEVICES) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(arrayOf(Manifest.permission.NEARBY_WIFI_DEVICES), 1001)
            }
        }
        
        // Request storage permissions for older Android versions (< 10)
        if (android.os.Build.VERSION.SDK_INT < 29) {
            if (checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(arrayOf(Manifest.permission.WRITE_EXTERNAL_STORAGE), 1002)
            }
        }
        
        // Acquire MulticastLock to allow mDNS discovery
        try {
            val wifiManager = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            multicastLock = wifiManager.createMulticastLock("VoidWarpMulticastLock").apply {
                setReferenceCounted(true)
            }
            multicastLock?.acquire()
        } catch (t: Throwable) {
            // Don't crash if device policy blocks multicast lock.
            Toast.makeText(this, "无法获取 MulticastLock，局域网发现可能不可用", Toast.LENGTH_LONG).show()
        }
        
        engine = VoidWarpEngine(android.os.Build.MODEL)
        transferManager = TransferManager(this)
        receiveManager = ReceiveManager()
        
        setContent {
            VoidWarpTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = AppColors.Background
                ) {
                    val context = LocalContext.current
                    val historyManager = remember { HistoryManager(context) }
                    var currentScreen by remember { mutableStateOf("main") }
                    
                    when (currentScreen) {
                        "main" -> MainScreen(
                            engine = engine!!,
                            transferManager = transferManager!!,
                            receiveManager = receiveManager!!,
                            historyManager = historyManager,
                            onNavigateToReceived = { currentScreen = "received" }
                        )
                        "received" -> ReceivedFilesScreen(
                            historyManager = historyManager,
                            onBack = { currentScreen = "main" }
                        )
                    }
                }
            }
        }
    }
    
    override fun onDestroy() {
        transferManager?.cancel()
        receiveManager?.close()
        engine?.close()
        try { multicastLock?.release() } catch (_: Throwable) {}
        super.onDestroy()
    }
}

data class PendingFile(
    val uri: Uri,
    val name: String,
    val size: Long
)

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(
    engine: VoidWarpEngine,
    transferManager: TransferManager,
    receiveManager: ReceiveManager,
    historyManager: HistoryManager,
    onNavigateToReceived: () -> Unit
) {
    val context = LocalContext.current
    val isDiscovering by engine.isDiscovering.collectAsState()
    val peers by engine.peers.collectAsState()
    var selectedPeer by remember { mutableStateOf<DiscoveredPeer?>(null) }
    var pendingFiles by remember { mutableStateOf<List<PendingFile>>(emptyList()) }
    val scope = rememberCoroutineScope()
    
    // Manual peer addition dialog state
    var showAddPeerDialog by remember { mutableStateOf(false) }
    var manualIp by remember { mutableStateOf("192.168.") }
    var manualPort by remember { mutableStateOf("42424") }
    
    // Collect manager states
    val receiverState by receiveManager.state.collectAsState()
    val receiverPort by receiveManager.port.collectAsState()
    
    // CRITICAL: Auto-initialize receiver on first launch
    // This ensures the receiver port is ready before discovery can start
    LaunchedEffect(Unit) {
        android.util.Log.d("MainScreen", "Initializing receiver on first launch...")
        if (receiverState == ReceiverState.IDLE) {
            // Initialize receiver first to get the port
            receiveManager.startReceiving()
            android.util.Log.d("MainScreen", "Receiver started on port: ${receiveManager.port.value}")
        }
    }
    
    // File picker launcher
    val filePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenMultipleDocuments()
    ) { uris: List<Uri> ->
        if (uris.isNotEmpty()) {
            val newList = pendingFiles.toMutableList()
            uris.forEach { uri ->
                var name = "未知文件"
                var size = 0L
                context.contentResolver.query(uri, null, null, null, null)?.use { cursor ->
                    if (cursor.moveToFirst()) {
                        val nameIndex = cursor.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
                        if (nameIndex >= 0) name = cursor.getString(nameIndex)
                        
                        val sizeIndex = cursor.getColumnIndex(android.provider.OpenableColumns.SIZE)
                        if (sizeIndex >= 0) size = cursor.getLong(sizeIndex)
                    }
                }
                newList.add(PendingFile(uri, name, size))
                
                try {
                    context.contentResolver.takePersistableUriPermission(
                        uri,
                        Intent.FLAG_GRANT_READ_URI_PERMISSION
                    )
                } catch (_: SecurityException) {}
            }
            pendingFiles = newList
        }
    }
    
    // Auto-refresh peers while discovering
    LaunchedEffect(isDiscovering) {
        while (isDiscovering) {
            engine.refreshPeers()
            delay(1000)
        }
    }
    
    Box(modifier = Modifier.fillMaxSize()) {
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(16.dp)
        ) {
            // Header
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(vertical = 12.dp),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Column {
                    Text(
                        text = "VoidWarp",
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold,
                        color = AppColors.Primary,
                        letterSpacing = 2.sp
                    )
                    
                    Text(
                        text = "设备ID: ${engine.deviceId.take(8).uppercase()}",
                        fontSize = 10.sp,
                        fontWeight = FontWeight.Medium,
                        color = Color.Gray,
                        modifier = Modifier
                            .background(AppColors.SurfaceVariant, RoundedCornerShape(4.dp))
                            .padding(horizontal = 6.dp, vertical = 2.dp)
                    )
                }
                
                IconButton(
                    onClick = onNavigateToReceived,
                    modifier = Modifier
                        .background(AppColors.SurfaceVariant, CircleShape)
                        .size(40.dp)
                ) {
                    Icon(
                        imageVector = Icons.Default.Description,
                        contentDescription = "已接收文件",
                        tint = Color.White,
                        modifier = Modifier.size(20.dp)
                    )
                }
            }
            
            // Receive Mode Switch (Clean Design)
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(16.dp),
                colors = CardDefaults.cardColors(containerColor = AppColors.Surface)
            ) {
                Row(
                    modifier = Modifier
                        .padding(16.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Box(
                        modifier = Modifier
                            .size(40.dp)
                            .background(
                                if (receiverState != ReceiverState.IDLE) AppColors.Success.copy(alpha = 0.2f) 
                                else AppColors.SurfaceVariant, 
                                CircleShape
                            ),
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(
                            imageVector = Icons.Default.Description, // Using Description as generic 'File' icon proxy
                            contentDescription = null,
                            tint = if (receiverState != ReceiverState.IDLE) AppColors.Success else Color.Gray
                        )
                    }
                    
                    Spacer(modifier = Modifier.width(16.dp))
                    
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = if (receiverState != ReceiverState.IDLE) "接收准备就绪" else "接收模式已关闭",
                            fontWeight = FontWeight.SemiBold,
                            color = Color.White
                        )
                        Text(
                            text = if (receiverState != ReceiverState.IDLE) "端口 $receiverPort 可见" else "点击开关启用",
                            fontSize = 12.sp,
                            color = Color.Gray
                        )
                    }
                    
                    Switch(
                        checked = receiverState != ReceiverState.IDLE,
                        onCheckedChange = { checked ->
                            if (checked) receiveManager.startReceiving() else receiveManager.stopReceiving()
                        },
                        colors = SwitchDefaults.colors(
                            checkedThumbColor = AppColors.Primary,
                            checkedTrackColor = AppColors.SurfaceLight
                        )
                    )
                }
            }
            
            Spacer(modifier = Modifier.height(16.dp))
            
            // Diagnostic Info (Debug)
            Card(
                modifier = Modifier.fillMaxWidth(),
                colors = CardDefaults.cardColors(containerColor = AppColors.SurfaceVariant),
                shape = RoundedCornerShape(8.dp)
            ) {
                Column(modifier = Modifier.padding(12.dp)) {
                    Text(
                        text = "诊断信息",
                        fontSize = 10.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color.Gray,
                        modifier = Modifier.padding(bottom = 4.dp)
                    )
                    
                    val ips = getAllIpAddresses()
                    Text(
                        text = "本机IP: ${if (ips.isEmpty()) "无" else ips.joinToString(", ")}",
                        fontSize = 11.sp,
                        color = Color.White,
                        lineHeight = 16.sp
                    )
                    
                    Text(
                        text = "发现: ${if(isDiscovering) "进行中" else "空闲"} | 监听: $receiverPort",
                        fontSize = 11.sp,
                        color = AppColors.Primary
                    )
                }
            }
            
            Spacer(modifier = Modifier.height(16.dp))
            
            // Radar / Device List Section
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = "附近设备",
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold,
                    color = Color.Gray,
                    letterSpacing = 1.sp,
                    modifier = Modifier.padding(bottom = 8.dp)
                )
                
                if (peers.isEmpty()) {
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .weight(1f),
                        contentAlignment = Alignment.Center
                    ) {
                        if (isDiscovering) {
                            // Radar Animation Placeholder (Simple circle pulse simulation)
                            val infiniteTransition = rememberInfiniteTransition()
                            val scale by infiniteTransition.animateFloat(
                                initialValue = 0.8f,
                                targetValue = 1.2f,
                                animationSpec = infiniteRepeatable(
                                    animation = tween(1500),
                                    repeatMode = RepeatMode.Reverse
                                )
                            )
                            
                            Box(
                                modifier = Modifier
                                    .size(120.dp)
                                    .scale(scale)
                                    .background(AppColors.Primary.copy(alpha = 0.1f), CircleShape)
                            )
                            
                            Icon(
                                imageVector = Icons.AutoMirrored.Filled.Send,
                                contentDescription = "正在扫描",
                                tint = AppColors.Primary,
                                modifier = Modifier.size(48.dp)
                            )
                            
                            Text(
                                text = "正在扫描网络...",
                                color = AppColors.Primary,
                                fontSize = 14.sp,
                                modifier = Modifier.padding(top = 100.dp)
                            )
                        } else {
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Icon(
                                    imageVector = Icons.Default.Link,
                                    contentDescription = "无设备",
                                    tint = Color.Gray,
                                    modifier = Modifier.size(64.dp)
                                )
                                Spacer(modifier = Modifier.height(16.dp))
                                Text("未发现设备", color = Color.Gray)
                                TextButton(onClick = { showAddPeerDialog = true }) {
                                    Text("手动添加", color = AppColors.Secondary)
                                }
                            }
                        }
                    }
                } else {
                    LazyColumn(
                        verticalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        items(peers) { peer ->
                            PeerItem(
                                peer = peer,
                                isSelected = peer == selectedPeer,
                                onClick = { selectedPeer = peer },
                                onTestLink = {
                                    scope.launch {
                                        Toast.makeText(context, "正在测试 ${peer.deviceName}...", Toast.LENGTH_SHORT).show()
                                        val result = engine.testConnection(peer)
                                        if (result) Toast.makeText(context, "设备在线", Toast.LENGTH_SHORT).show()
                                        else Toast.makeText(context, "连接失败", Toast.LENGTH_SHORT).show()
                                    }
                                }
                            )
                        }
                    }
                }
            }
            
            Spacer(modifier = Modifier.height(16.dp))
            
            // File Selection & Send Area
            Card(
                shape = RoundedCornerShape(topStart = 24.dp, topEnd = 24.dp),
                colors = CardDefaults.cardColors(containerColor = AppColors.SurfaceVariant),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(modifier = Modifier.padding(20.dp)) {
                    // File Selector / Queue
                    if (pendingFiles.isEmpty()) {
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .clickable { filePickerLauncher.launch(arrayOf("*/*")) }
                                .background(AppColors.Background, RoundedCornerShape(12.dp))
                                .padding(16.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Icon(
                                imageVector = Icons.Default.Add,
                                contentDescription = null,
                                tint = Color.Gray
                            )
                            Spacer(modifier = Modifier.width(12.dp))
                            Text(
                                text = "选择要发送的文件",
                                color = Color.Gray,
                                modifier = Modifier.weight(1f)
                            )
                        }
                    } else {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .background(AppColors.Background, RoundedCornerShape(12.dp))
                                .padding(8.dp)
                        ) {
                            Text(
                                text = "待发送队列 (${pendingFiles.size})",
                                fontSize = 12.sp,
                                color = AppColors.Primary,
                                modifier = Modifier.padding(start = 8.dp, bottom = 4.dp)
                            )
                            
                            LazyColumn(
                                modifier = Modifier.heightIn(max = 200.dp),
                                verticalArrangement = Arrangement.spacedBy(4.dp)
                            ) {
                                items(pendingFiles) { file ->
                                    Row(
                                        modifier = Modifier
                                            .fillMaxWidth()
                                            .background(AppColors.Surface, RoundedCornerShape(8.dp))
                                            .padding(horizontal = 12.dp, vertical = 8.dp),
                                        verticalAlignment = Alignment.CenterVertically
                                    ) {
                                        Icon(Icons.Default.Description, null, tint = AppColors.Primary, modifier = Modifier.size(20.dp))
                                        Spacer(Modifier.width(8.dp))
                                        Column(Modifier.weight(1f)) {
                                            Text(file.name, color = Color.White, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                                            Text(formatFileSize(file.size), color = Color.Gray, fontSize = 10.sp)
                                        }
                                        IconButton(
                                            onClick = { pendingFiles = pendingFiles.filter { it != file } },
                                            modifier = Modifier.size(24.dp)
                                        ) {
                                            Icon(Icons.Default.Close, null, tint = Color.Gray, modifier = Modifier.size(16.dp))
                                        }
                                    }
                                }
                                
                                item {
                                    TextButton(
                                        onClick = { filePickerLauncher.launch(arrayOf("*/*")) },
                                        modifier = Modifier.fillMaxWidth()
                                    ) {
                                        Icon(Icons.Default.Add, null, modifier = Modifier.size(16.dp))
                                        Spacer(Modifier.width(4.dp))
                                        Text("继续添加", fontSize = 12.sp)
                                    }
                                }
                            }
                        }
                    }
                    
                    Spacer(modifier = Modifier.height(16.dp))
                    
                    // Buttons
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        // Scan Toggle
                        Button(
                            onClick = {
                                if (isDiscovering) {
                                    engine.stopDiscovery()
                                } else {
                                        scope.launch(Dispatchers.IO) {
                                            // Ensure receiver is ready and get the actual port
                                            // This prevents the race condition where discovery starts with port 0 or default
                                            val actualPort = receiveManager.ensureStarted()
                                            
                                            android.util.Log.d("MainScreen", "Starting discovery with verified receiver port: $actualPort")
                                            
                                            val portToUse = if (actualPort > 0) actualPort else 42424
                                            engine.startDiscovery(portToUse)
                                        }
                                }
                            },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = if (isDiscovering) AppColors.SurfaceLight else AppColors.Surface
                            ),
                            shape = RoundedCornerShape(12.dp),
                            modifier = Modifier.weight(1f).height(50.dp)
                        ) {
                            Text(if (isDiscovering) "停止" else "扫描")
                        }
                        
                        // Send Action
                        Button(
                            onClick = {
                                if (selectedPeer != null && pendingFiles.isNotEmpty()) {
                                    scope.launch {
                                        val total = pendingFiles.size
                                        var successCount = 0
                                        var errorMsg: String? = null
                                        
                                        val filesToSend = pendingFiles.toList()
                                        // Clear queue immediately or after? Better after to show progress if we had per-file progress in queue
                                        // But for now let's just send them one by one.
                                        
                                        filesToSend.forEachIndexed { index, file ->
                                            Toast.makeText(context, "正在发送 (${index + 1}/$total): ${file.name}", Toast.LENGTH_SHORT).show()
                                            
                                            val complete = CompletableDeferred<Boolean>()
                                            transferManager.sendFile(file.uri, selectedPeer!!, onComplete = { s, e ->
                                                if (s) successCount++ else errorMsg = e
                                                complete.complete(s)
                                            })
                                            complete.await()
                                        }
                                        
                                        if (successCount == total) {
                                            Toast.makeText(context, "所有文件发送完成 ($total 个)", Toast.LENGTH_LONG).show()
                                            pendingFiles = emptyList() // Clear only on full success? Or always?
                                        } else {
                                            Toast.makeText(context, "部分发送失败: $successCount/$total 成功. 错误: $errorMsg", Toast.LENGTH_LONG).show()
                                            // Keep failed ones in queue? Implementation simple: clear only successful ones or leave as is.
                                            // Let's clear the successful ones.
                                            pendingFiles = pendingFiles.filter { f -> !filesToSend.take(successCount).contains(f) }
                                        }
                                    }
                                } else {
                                    Toast.makeText(context, "请先选择文件和设备", Toast.LENGTH_SHORT).show()
                                }
                            },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = AppColors.Primary,
                                disabledContainerColor = AppColors.Surface
                            ),
                            enabled = selectedPeer != null && pendingFiles.isNotEmpty(),
                            shape = RoundedCornerShape(12.dp),
                            modifier = Modifier.weight(2f).height(50.dp)
                        ) {
                            Icon(Icons.AutoMirrored.Filled.Send, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(8.dp))
                            Text(if (pendingFiles.size > 1) "发送 ${pendingFiles.size} 个文件" else "开始传送")
                        }
                    }
                }
            }
        
        // FAB removed to avoid blocking content
        }
    }
    
    // Incoming Transfer Dialog
    val pendingTransfer by receiveManager.pendingTransfer.collectAsState()
    
    // Prevent double-click issues
    var isProcessing by remember { mutableStateOf(false) }
    // Reset processing state when dialog appears/disappears
    LaunchedEffect(pendingTransfer) {
        isProcessing = false
    }
    
    if (pendingTransfer != null) {
        val transfer = pendingTransfer!!
        AlertDialog(
            onDismissRequest = {
                // Do nothing, force user to choose
            },
            containerColor = AppColors.Surface,
            title = {
                Text(if (transfer.isFolder) "接收文件夹请求" else "接收文件请求", color = Color.White)
            },
            text = {
                Column {
                    Text(
                        text = "来自: ${transfer.senderName}",
                        color = Color.White,
                        fontWeight = FontWeight.Bold
                    )
                    Text(
                        text = "IP: ${transfer.senderAddress}",
                        color = Color.Gray,
                        fontSize = 12.sp
                    )
                    Spacer(modifier = Modifier.height(8.dp))
                    val typeLabel = if (transfer.isFolder) "文件夹" else "文件"
                    val sizeLabel = if (transfer.isFolder) "总大小" else "大小"
                    
                    Text(
                        text = "$typeLabel: ${transfer.fileName}",
                        color = Color.White
                    )
                    Text(
                        text = "$sizeLabel: ${transfer.formattedSize}",
                        color = Color.Gray,
                        fontSize = 12.sp
                    )
                }
            },
            confirmButton = {
                Button(
                    enabled = !isProcessing,
                    onClick = {
                        if (isProcessing) return@Button
                        isProcessing = true
                        
                        scope.launch {
                            try {
                                // 1. RECEIVE TO SANDBOX (100% Guaranteed Success)
                                // Use private app storage which doesn't require permissions
                                val sandboxDir = context.getExternalFilesDir(null) ?: context.filesDir
                                
                                // Sanitize filename
                                val fileName = transfer.fileName
                                val safeName = fileName.replace("[^a-zA-Z0-9._\\- (\\[\\])]".toRegex(), "_")
                                var sandboxFile = File(sandboxDir, safeName)
                                
                                // Handle duplicate filenames in sandbox
                                var counter = 1
                                while (sandboxFile.exists()) {
                                    val nameWithoutExt = safeName.substringBeforeLast('.')
                                    val ext = if (safeName.contains('.')) ".${safeName.substringAfterLast('.')}" else ""
                                    sandboxFile = File(sandboxDir, "$nameWithoutExt ($counter)$ext")
                                    counter++
                                }
                                
                                val sandboxPath = sandboxFile.absolutePath
                                android.util.Log.i("VoidWarp", "Step 1: Receiving to Sandbox: $sandboxPath")
                                
                                val success = receiveManager.acceptTransfer(sandboxPath)
                                if (success) {
                                    // 2. MOVE TO PUBLIC STORAGE (Gallery/Downloads)
                                    withContext(Dispatchers.IO) {
                                        try {
                                            copyToPublicStorage(context, sandboxFile, transfer.fileName)
                                        } catch (e: Throwable) {
                                            android.util.Log.e("VoidWarp", "Copy to public storage failed: $e")
                                            withContext(Dispatchers.Main) {
                                                Toast.makeText(context, "保存到相册失败: ${e.message}", Toast.LENGTH_SHORT).show()
                                            }
                                        }
                                        
                                        // 3. ADD TO HISTORY (Move to IO thread to prevent ANR/Crash)
                                        try {
                                            historyManager.add(
                                                HistoryItem(
                                                    fileName = transfer.fileName,
                                                    filePath = sandboxPath,
                                                    fileSize = transfer.fileSize,
                                                    senderName = transfer.senderName,
                                                    receivedTime = System.currentTimeMillis()
                                                )
                                            )
                                        } catch (e: Throwable) {
                                            android.util.Log.e("VoidWarp", "Failed to add to history: $e")
                                        }
                                    }
                                } else {
                                    withContext(Dispatchers.Main) {
                                        Toast.makeText(context, "接收失败: 传输错误", Toast.LENGTH_LONG).show()
                                    }
                                }
                            } catch (e: Throwable) {
                                android.util.Log.e("VoidWarp", "CRITICAL ERROR in Receive: $e")
                                withContext(Dispatchers.Main) {
                                    Toast.makeText(context, "致命错误: ${e.message}", Toast.LENGTH_LONG).show()
                                }
                            } finally {
                                withContext(Dispatchers.Main) {
                                    isProcessing = false
                                }
                            }
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))
                ) {
                    Text(if (isProcessing) "处理中..." else "接收")
                }
            },
            dismissButton = {
                TextButton(
                    enabled = !isProcessing,
                    onClick = {
                        if (isProcessing) return@TextButton
                        receiveManager.rejectTransfer()
                    }
                ) {
                    Text("拒绝", color = Color(0xFFFF5252))
                }
            }
        )
    }
    
    // Manual Peer Addition Dialog
    if (showAddPeerDialog) {
        var showAdvanced by remember { mutableStateOf(false) }
        
        // IP Validation
        val isIpValid = remember(manualIp) {
            manualIp.isNotBlank() && android.util.Patterns.IP_ADDRESS.matcher(manualIp).matches()
        }

        AlertDialog(
            onDismissRequest = { showAddPeerDialog = false },
            containerColor = AppColors.Surface,
            title = {
                Text("手动添加设备", color = Color.White)
            },
            text = {
                Column(
                    verticalArrangement = Arrangement.spacedBy(12.dp)
                ) {
                    Text(
                        text = "当自动扫描未找到设备时，输入该设备的 IP 地址来手动连接。",
                        color = Color.Gray,
                        fontSize = 12.sp,
                        lineHeight = 16.sp
                    )
                    
                    OutlinedTextField(
                        value = manualIp,
                        onValueChange = { manualIp = it },
                        label = { Text("IP 地址", color = Color.Gray) },
                        singleLine = true,
                        isError = manualIp.isNotBlank() && !isIpValid,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedTextColor = Color.White,
                            unfocusedTextColor = Color.White,
                            focusedBorderColor = AppColors.Primary,
                            unfocusedBorderColor = Color.Gray,
                            errorBorderColor = AppColors.Error,
                            cursorColor = AppColors.Primary
                        ),
                        modifier = Modifier.fillMaxWidth()
                    )
                    
                    // Advanced Settings Toggle
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clip(RoundedCornerShape(4.dp))
                            .clickable { showAdvanced = !showAdvanced }
                            .padding(vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(
                            text = "高级设置",
                            color = AppColors.Primary,
                            fontSize = 14.sp
                        )
                        Icon(
                            imageVector = if (showAdvanced) Icons.Default.KeyboardArrowUp else Icons.Default.KeyboardArrowDown,
                            contentDescription = null,
                            tint = AppColors.Primary,
                            modifier = Modifier.padding(start = 4.dp).size(20.dp)
                        )
                    }

                    if (showAdvanced) {
                        OutlinedTextField(
                            value = manualPort,
                            onValueChange = { manualPort = it.filter { c -> c.isDigit() } },
                            label = { Text("端口", color = Color.Gray) },
                            singleLine = true,
                            colors = OutlinedTextFieldDefaults.colors(
                                focusedTextColor = Color.White,
                                unfocusedTextColor = Color.White,
                                focusedBorderColor = AppColors.Primary,
                                unfocusedBorderColor = Color.Gray,
                                cursorColor = AppColors.Primary
                            ),
                            modifier = Modifier.fillMaxWidth()
                        )
                        Text(
                            text = "提示: 不确定请使用默认端口 42424",
                            color = Color.Gray,
                            fontSize = 10.sp
                        )
                    }
                    
                    Text(
                        text = "提示: USB连接时使用 127.0.0.1 (需先运行 adb reverse tcp:42424 tcp:42424)",
                        color = Color(0xFF888888),
                        fontSize = 10.sp
                    )
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        val portNum = manualPort.toIntOrNull() ?: 42424
                        if (isIpValid) {
                            engine.addManualPeer(
                                id = "manual-${manualIp.replace(".", "-")}",
                                name = "手动添加 ($manualIp)",
                                ip = manualIp.trim(),
                                port = portNum
                            )
                            engine.refreshPeers()
                            Toast.makeText(context, "已添加设备: $manualIp:$portNum", Toast.LENGTH_SHORT).show()
                            showAddPeerDialog = false
                        }
                    },
                    enabled = isIpValid,
                    colors = ButtonDefaults.buttonColors(
                        containerColor = AppColors.Primary,
                        disabledContainerColor = AppColors.SurfaceLight
                    )
                ) {
                    Text("添加")
                }
            },
            dismissButton = {
                TextButton(onClick = { showAddPeerDialog = false }) {
                    Text("取消", color = Color.Gray)
                }
            }
        )
    }
}

@Composable
fun PeerItem(
    peer: DiscoveredPeer,
    isSelected: Boolean,
    onClick: () -> Unit,
    onTestLink: () -> Unit
) {
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp)
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(8.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (isSelected) AppColors.SurfaceLight else AppColors.Surface
        )
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(12.dp),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween
        ) {
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = peer.deviceName,
                    color = Color.White,
                    fontWeight = FontWeight.Medium
                )
                Text(
                    text = peer.displayName,
                    color = Color.Gray,
                    fontSize = 12.sp
                )
            }
            
            IconButton(onClick = onTestLink) {
                Icon(
                    imageVector = Icons.Default.Link,
                    contentDescription = "测试连接",
                    tint = AppColors.Primary
                )
            }
        }
    }
}

// App Colors
object AppColors {
    val Background = Color(0xFF1A1A2E)
    val Surface = Color(0xFF16213E)
    val SurfaceLight = Color(0xFF4A4E69)
    val SurfaceVariant = Color(0xFF2A2A4A)
    val Primary = Color(0xFF6C63FF)
    val Secondary = Color(0xFFFF9800)
    val Success = Color(0xFF4CAF50)
    val Error = Color(0xFFFF5252)
}

fun getAllIpAddresses(): List<String> {
    val ips = mutableListOf<String>()
    try {
        val interfaces = java.net.NetworkInterface.getNetworkInterfaces()
        while (interfaces.hasMoreElements()) {
            val intf = interfaces.nextElement()
            if (intf.isLoopback || !intf.isUp) continue
            
            val addrs = intf.inetAddresses
            while (addrs.hasMoreElements()) {
                val addr = addrs.nextElement()
                if (addr is java.net.Inet4Address && !addr.isLoopbackAddress) {
                    ips.add("${intf.displayName}: ${addr.hostAddress}")
                }
            }
        }
    } catch (_: Exception) {}
    return ips
}
fun formatFileSize(size: Long): String {
    return when {
        size >= 1024 * 1024 * 1024 -> "%.2f GB".format(size / 1024.0 / 1024.0 / 1024.0)
        size >= 1024 * 1024 -> "%.1f MB".format(size / 1024.0 / 1024.0)
        size >= 1024 -> "%.1f KB".format(size / 1024.0)
        else -> "$size B"
    }
}

// Helper to copy file from Sandbox to Public Storage via MediaStore
suspend fun copyToPublicStorage(context: Context, sandboxFile: File, originalName: String) {
    if (!sandboxFile.exists()) return
    
    val extension = originalName.substringAfterLast('.', "").lowercase()
    val mimeType = when (extension) {
        "jpg", "jpeg" -> "image/jpeg"
        "png" -> "image/png"
        "gif" -> "image/gif"
        "mp4" -> "video/mp4"
        "mp3" -> "audio/mpeg"
        "pdf" -> "application/pdf"
        "zip" -> "application/zip"
        "apk" -> "application/vnd.android.package-archive"
        else -> "*/*"
    }

    var success = false
    var typeMsg = "下载/VoidWarp"

    try {
        val resolver = context.contentResolver
        val contentValues = android.content.ContentValues().apply {
            put(android.provider.MediaStore.MediaColumns.DISPLAY_NAME, originalName)
            put(android.provider.MediaStore.MediaColumns.MIME_TYPE, mimeType)
            if (android.os.Build.VERSION.SDK_INT >= 29) {
                // Use strings for keys to avoid classloader issues on old APIs
                put("relative_path", when (extension) {
                    "jpg", "jpeg", "png", "gif", "bmp", "webp", "heic" -> "Pictures/VoidWarp"
                    "mp4", "mkv", "avi", "mov", "wmv" -> "Movies/VoidWarp"
                    "mp3", "wav", "ogg" -> "Music/VoidWarp"
                    else -> "Download/VoidWarp"
                })
                put("is_pending", 1)
            }
        }

        // Determine collection URI safely
        val collection = when (extension) {
            "jpg", "jpeg", "png", "gif", "bmp", "webp", "heic" -> {
                typeMsg = "相册"
                if (android.os.Build.VERSION.SDK_INT >= 29) android.provider.MediaStore.Images.Media.getContentUri("external_primary")
                else android.provider.MediaStore.Images.Media.EXTERNAL_CONTENT_URI
            }
            "mp4", "mkv", "avi", "mov", "wmv" -> {
                typeMsg = "视频"
                if (android.os.Build.VERSION.SDK_INT >= 29) android.provider.MediaStore.Video.Media.getContentUri("external_primary")
                else android.provider.MediaStore.Video.Media.EXTERNAL_CONTENT_URI
            }
            "mp3", "wav", "ogg" -> {
                typeMsg = "音乐"
                if (android.os.Build.VERSION.SDK_INT >= 29) android.provider.MediaStore.Audio.Media.getContentUri("external_primary")
                else android.provider.MediaStore.Audio.Media.EXTERNAL_CONTENT_URI
            }
            else -> {
                typeMsg = "下载/VoidWarp"
                if (android.os.Build.VERSION.SDK_INT >= 29) {
                    // Avoid direct reference to MediaStore.Downloads to prevent crash on API < 29
                    android.net.Uri.parse("content://media/external/downloads")
                } else {
                    @Suppress("DEPRECATION")
                    android.provider.MediaStore.Files.getContentUri("external")
                }
            }
        }

        val uri = resolver.insert(collection, contentValues)
        if (uri != null) {
            resolver.openOutputStream(uri)?.use { outputStream ->
                java.io.FileInputStream(sandboxFile).use { inputStream ->
                    inputStream.copyTo(outputStream, bufferSize = 128 * 1024)
                }
            }
            
            if (android.os.Build.VERSION.SDK_INT >= 29) {
                contentValues.clear()
                contentValues.put("is_pending", 0)
                resolver.update(uri, contentValues, null, null)
            }
            success = true
            android.util.Log.i("VoidWarp", "Step 2: Copied to Public Storage: $uri")
        } else {
            android.util.Log.e("VoidWarp", "Step 2 Failed: Insert returned null uri")
        }
    } catch (e: Throwable) {
        // Catch Throwable to handle NoClassDefFoundError etc.
        android.util.Log.e("VoidWarp", "Step 2 CRITICAL ERROR: $e")
    }

    withContext(Dispatchers.Main) {
        if (success) {
            Toast.makeText(context, "接收成功！已保存到$typeMsg", Toast.LENGTH_LONG).show()
        } else {
            Toast.makeText(context, "已保存到应用私有目录 (沙盒)", Toast.LENGTH_LONG).show()
        }
    }
}
