package com.voidwarp.android

import android.Manifest
import android.content.pm.PackageManager
import android.net.Uri
import android.net.wifi.WifiManager
import android.content.Context
import android.os.Bundle
import android.widget.Toast
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
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
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.voidwarp.android.core.*
import com.voidwarp.android.ui.theme.VoidWarpTheme
import com.voidwarp.android.ui.ReceivedFilesScreen
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

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
                    var currentScreen by remember { mutableStateOf("main") }
                    
                    when (currentScreen) {
                        "main" -> MainScreen(
                            engine = engine!!,
                            transferManager = transferManager!!,
                            receiveManager = receiveManager!!,
                            onNavigateToReceived = { currentScreen = "received" }
                        )
                        "received" -> ReceivedFilesScreen(
                            onBack = { currentScreen = "main" }
                        )
                    }
                }
            }
        }
    }
    
    override fun onDestroy() {
        receiveManager?.close()
        engine?.close()
        try { multicastLock?.release() } catch (_: Throwable) {}
        super.onDestroy()
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun MainScreen(
    engine: VoidWarpEngine,
    transferManager: TransferManager,
    receiveManager: ReceiveManager,
    onNavigateToReceived: () -> Unit
) {
    val context = LocalContext.current
    val isDiscovering by engine.isDiscovering.collectAsState()
    val peers by engine.peers.collectAsState()
    var selectedPeer by remember { mutableStateOf<DiscoveredPeer?>(null) }
    var selectedFileUri by remember { mutableStateOf<Uri?>(null) }
    var selectedFileName by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    
    // Manual peer addition dialog state
    var showAddPeerDialog by remember { mutableStateOf(false) }
    var manualIp by remember { mutableStateOf("192.168.") }
    var manualPort by remember { mutableStateOf("42424") }
    
    // Collect manager states
    val isReceiveMode by receiveManager.state.collectAsState()
    val receiverPort by receiveManager.port.collectAsState()
    
    // File picker launcher
    val filePickerLauncher = rememberLauncherForActivityResult(
        contract = ActivityResultContracts.OpenDocument()
    ) { uri: Uri? ->
        uri?.let {
            selectedFileUri = it
            // Get file name from URI
            val cursor = context.contentResolver.query(it, null, null, null, null)
            cursor?.use { c ->
                if (c.moveToFirst()) {
                    val nameIndex = c.getColumnIndex(android.provider.OpenableColumns.DISPLAY_NAME)
                    if (nameIndex >= 0) {
                        selectedFileName = c.getString(nameIndex)
                    }
                }
            }
            // Status now handled by TransferManager
        }
    }
    
    // Auto-refresh peers
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
                        text = "VOID WARP",
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold,
                        color = AppColors.Primary,
                        letterSpacing = 2.sp
                    )
                    
                    Text(
                        text = "ID: ${engine.deviceId.take(8).uppercase()}",
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
                        contentDescription = "Received Files",
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
                                if (isReceiveMode != ReceiverState.IDLE) AppColors.Success.copy(alpha = 0.2f) 
                                else AppColors.SurfaceVariant, 
                                CircleShape
                            ),
                        contentAlignment = Alignment.Center
                    ) {
                        Icon(
                            imageVector = Icons.Default.Description, // Using Description as generic 'File' icon proxy
                            contentDescription = null,
                            tint = if (isReceiveMode != ReceiverState.IDLE) AppColors.Success else Color.Gray
                        )
                    }
                    
                    Spacer(modifier = Modifier.width(16.dp))
                    
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = if (isReceiveMode != ReceiverState.IDLE) "Ready to Receive" else "Receive Mode Off",
                            fontWeight = FontWeight.SemiBold,
                            color = Color.White
                        )
                        Text(
                            text = if (isReceiveMode != ReceiverState.IDLE) "Visible on port $receiverPort" else "Tap switch to enable",
                            fontSize = 12.sp,
                            color = Color.Gray
                        )
                    }
                    
                    Switch(
                        checked = isReceiveMode != ReceiverState.IDLE,
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
                        text = "DIAGNOSTICS",
                        fontSize = 10.sp,
                        fontWeight = FontWeight.Bold,
                        color = Color.Gray,
                        modifier = Modifier.padding(bottom = 4.dp)
                    )
                    
                    // Show all IPs
                    val ips = getAllIpAddresses()
                    Text(
                        text = "My IPs: ${ips.joinToString(", ")}",
                        fontSize = 11.sp,
                        color = Color.White,
                        lineHeight = 16.sp
                    )
                    
                    Text(
                        text = "Discovery: ${if(isDiscovering) "Active" else "Idle"} | Listening: $receiverPort",
                        fontSize = 11.sp,
                        color = AppColors.Primary
                    )
                }
            }
            
            Spacer(modifier = Modifier.height(16.dp))
            
            // Radar / Device List Section
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = "NEARBY DEVICES",
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
                                imageVector = Icons.AutoMirrored.Filled.Send, // Placeholder for Radar
                                contentDescription = "Scanning",
                                tint = AppColors.Primary,
                                modifier = Modifier.size(48.dp)
                            )
                            
                            Text(
                                text = "Scanning network...",
                                color = AppColors.Primary,
                                fontSize = 14.sp,
                                modifier = Modifier.padding(top = 100.dp)
                            )
                        } else {
                            Column(horizontalAlignment = Alignment.CenterHorizontally) {
                                Icon(
                                    imageVector = Icons.Default.Link,
                                    contentDescription = "No peers",
                                    tint = Color.Gray,
                                    modifier = Modifier.size(64.dp)
                                )
                                Spacer(modifier = Modifier.height(16.dp))
                                Text("No devices found", color = Color.Gray)
                                TextButton(onClick = { showAddPeerDialog = true }) {
                                    Text("Add Manually", color = AppColors.Secondary)
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
                                        Toast.makeText(context, "Pinging ${peer.deviceName}...", Toast.LENGTH_SHORT).show()
                                        val result = engine.testConnection(peer)
                                        if (result) Toast.makeText(context, "Online", Toast.LENGTH_SHORT).show()
                                        else Toast.makeText(context, "Unreachable", Toast.LENGTH_SHORT).show()
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
                    // File Selector
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { filePickerLauncher.launch(arrayOf("*/*")) }
                            .background(AppColors.Background, RoundedCornerShape(12.dp))
                            .padding(16.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Icon(
                            imageVector = if (selectedFileName != null) Icons.Default.Description else Icons.Default.Add,
                            contentDescription = null,
                            tint = if (selectedFileName != null) AppColors.Primary else Color.Gray
                        )
                        Spacer(modifier = Modifier.width(12.dp))
                        Text(
                            text = selectedFileName ?: "Select File to Send",
                            color = if (selectedFileName != null) Color.White else Color.Gray,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.weight(1f)
                        )
                    }
                    
                    Spacer(modifier = Modifier.height(16.dp))
                    
                    // Buttons
                    Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        // Scan Toggle
                        Button(
                            onClick = {
                                if (isDiscovering) engine.stopDiscovery() else {
                                    scope.launch(Dispatchers.IO) {
                                        val port = if (receiverPort > 0) receiverPort else 42424
                                        engine.startDiscovery(port)
                                    }
                                }
                            },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = if (isDiscovering) AppColors.SurfaceLight else AppColors.Surface
                            ),
                            shape = RoundedCornerShape(12.dp),
                            modifier = Modifier.weight(1f).height(50.dp)
                        ) {
                            Text(if (isDiscovering) "Stop" else "Scan")
                        }
                        
                        // Send Action
                        Button(
                            onClick = {
                                if (selectedPeer != null && selectedFileUri != null) {
                                    scope.launch {
                                        transferManager.sendFile(selectedFileUri!!, selectedPeer!!, onComplete = { s, e ->
                                            scope.launch { Toast.makeText(context, if(s) "Sent!" else "Error: $e", Toast.LENGTH_SHORT).show() }
                                        })
                                    }
                                } else {
                                    Toast.makeText(context, "Select file and peer first", Toast.LENGTH_SHORT).show()
                                }
                            },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = AppColors.Primary,
                                disabledContainerColor = AppColors.Surface
                            ),
                            enabled = selectedPeer != null && selectedFileUri != null,
                            shape = RoundedCornerShape(12.dp),
                            modifier = Modifier.weight(2f).height(50.dp)
                        ) {
                            Icon(Icons.AutoMirrored.Filled.Send, null, Modifier.size(18.dp))
                            Spacer(Modifier.width(8.dp))
                            Text("WARP FILE")
                        }
                    }
                }
            }
        
        // FAB removed to avoid blocking content
        }
    }
    
    // Incoming Transfer Dialog
    val pendingTransfer by receiveManager.pendingTransfer.collectAsState()
    
    if (pendingTransfer != null) {
        val transfer = pendingTransfer!!
        AlertDialog(
            onDismissRequest = {
                // Do nothing, force user to choose
            },
            containerColor = AppColors.Surface,
            title = {
                Text("接收文件请求", color = Color.White)
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
                    Text(
                        text = "文件: ${transfer.fileName}",
                        color = Color.White
                    )
                    Text(
                        text = "大小: ${transfer.formattedSize}",
                        color = Color.Gray,
                        fontSize = 12.sp
                    )
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        scope.launch {
                            // Define save path: App-Specific Downloads directory
                            // This works on all Android versions without extra permissions
                            val downloadDir = context.getExternalFilesDir(android.os.Environment.DIRECTORY_DOWNLOADS)
                            // Fallback to app root if null (rare)
                            val saveDir = if (downloadDir != null) downloadDir else context.filesDir
                            
                            if (!saveDir.exists()) {
                                saveDir.mkdirs()
                            }
                            // Sanitize filename lightly
                            val safeName = transfer.fileName.replace("[^a-zA-Z0-9._-]".toRegex(), "_")
                            val savePath = java.io.File(saveDir, safeName).absolutePath
                            
                            val success = receiveManager.acceptTransfer(savePath)
                            if (success) {
                                Toast.makeText(context, "接收成功: $savePath", Toast.LENGTH_LONG).show()
                            } else {
                                Toast.makeText(context, "接收失败", Toast.LENGTH_LONG).show()
                            }
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4CAF50))
                ) {
                    Text("接收")
                }
            },
            dismissButton = {
                TextButton(
                    onClick = {
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
                        text = "输入Windows设备的IP地址和端口",
                        color = Color.Gray,
                        fontSize = 12.sp
                    )
                    
                    OutlinedTextField(
                        value = manualIp,
                        onValueChange = { manualIp = it },
                        label = { Text("IP 地址", color = Color.Gray) },
                        singleLine = true,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedTextColor = Color.White,
                            unfocusedTextColor = Color.White,
                            focusedBorderColor = Color(0xFF6C63FF),
                            unfocusedBorderColor = Color.Gray
                        ),
                        modifier = Modifier.fillMaxWidth()
                    )
                    
                    OutlinedTextField(
                        value = manualPort,
                        onValueChange = { manualPort = it.filter { c -> c.isDigit() } },
                        label = { Text("端口", color = Color.Gray) },
                        singleLine = true,
                        colors = OutlinedTextFieldDefaults.colors(
                            focusedTextColor = Color.White,
                            unfocusedTextColor = Color.White,
                            focusedBorderColor = Color(0xFF6C63FF),
                            unfocusedBorderColor = Color.Gray
                        ),
                        modifier = Modifier.fillMaxWidth()
                    )
                    
                    Text(
                        text = "提示: USB连接时使用 127.0.0.1:42424\n(需先在PC上运行 adb reverse tcp:42424 tcp:42424)",
                        color = Color(0xFF888888),
                        fontSize = 10.sp
                    )
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        val portNum = manualPort.toIntOrNull() ?: 42424
                        if (manualIp.isNotBlank()) {
                            engine.addManualPeer(
                                id = "manual-${manualIp.replace(".", "-")}",
                                name = "手动添加 ($manualIp)",
                                ip = manualIp.trim(),
                                port = portNum
                            )
                            engine.refreshPeers()
                            Toast.makeText(context, "已添加设备: $manualIp:$portNum", Toast.LENGTH_SHORT).show()
                            showAddPeerDialog = false
                        } else {
                            Toast.makeText(context, "请输入有效的IP地址", Toast.LENGTH_SHORT).show()
                        }
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF6C63FF))
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
