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
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.automirrored.filled.Send
import androidx.compose.material3.*
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
                    color = Color(0xFF1A1A2E)
                ) {
                    MainScreen(
                        engine = engine!!,
                        transferManager = transferManager!!,
                        receiveManager = receiveManager!!
                    )
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
    receiveManager: ReceiveManager
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
    val transferProgress by transferManager.progress.collectAsState()
    val statusText by transferManager.statusMessage.collectAsState()
    
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
            Text(
                text = "VoidWarp",
                fontSize = 28.sp,
                fontWeight = FontWeight.Bold,
                color = Color(0xFF6C63FF)
            )
            
            Text(
                text = "设备 ID: ${engine.deviceId.take(8)}...",
                fontSize = 12.sp,
                color = Color.Gray,
                modifier = Modifier.padding(top = 4.dp)
            )
            
            Spacer(modifier = Modifier.height(16.dp))
            
            // Receive Mode Toggle
            Card(
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(8.dp),
                colors = CardDefaults.cardColors(containerColor = Color(0xFF16213E))
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 12.dp),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Column {
                        Text(
                            text = "接收模式",
                            color = Color.White,
                            fontSize = 14.sp
                        )
                        if (isReceiveMode != ReceiverState.IDLE) {
                            Text(
                                text = "端口: $receiverPort",
                                color = Color.Gray,
                                fontSize = 11.sp
                            )
                        }
                    }
                    Switch(
                        checked = isReceiveMode != ReceiverState.IDLE,
                        onCheckedChange = { checked ->
                            if (checked) {
                                receiveManager.startReceiving()
                            } else {
                                receiveManager.stopReceiving()
                            }
                        },
                        colors = SwitchDefaults.colors(
                            checkedThumbColor = Color(0xFF6C63FF),
                            checkedTrackColor = Color(0xFF4A4E69)
                        )
                    )
                }
            }
            
            Spacer(modifier = Modifier.height(12.dp))
            
            // Discovery Status
            Row(
                verticalAlignment = Alignment.CenterVertically,
                modifier = Modifier.padding(bottom = 8.dp)
            ) {
                Box(
                    modifier = Modifier
                        .size(10.dp)
                        .clip(CircleShape)
                        .background(if (isDiscovering) Color(0xFF6C63FF) else Color.Gray)
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    text = if (isDiscovering) "已发现 ${peers.size} 个设备" else "发现已停止",
                    color = Color.LightGray,
                    fontSize = 13.sp
                )
            }
            
            // Device List
            Card(
                modifier = Modifier
                    .fillMaxWidth()
                    .weight(1f),
                shape = RoundedCornerShape(12.dp),
                colors = CardDefaults.cardColors(containerColor = Color(0xFF16213E))
            ) {
                LazyColumn(
                    modifier = Modifier.padding(8.dp)
                ) {
                    items(peers) { peer ->
                        PeerItem(
                            peer = peer,
                            isSelected = peer == selectedPeer,
                            onClick = { selectedPeer = peer }
                        )
                    }
                    
                    if (peers.isEmpty()) {
                        item {
                            Text(
                                text = if (isDiscovering) "正在搜索..." else "点击下方按钮开始发现设备",
                                color = Color.Gray,
                                modifier = Modifier.padding(16.dp)
                            )
                        }
                    }
                }
            }
            
            Spacer(modifier = Modifier.height(12.dp))
            
            // Selected file indicator
            if (selectedFileName != null) {
                Card(
                    modifier = Modifier.fillMaxWidth(),
                    shape = RoundedCornerShape(8.dp),
                    colors = CardDefaults.cardColors(containerColor = Color(0xFF2A2A4A))
                ) {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(12.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Text(text = "📄", fontSize = 20.sp)
                        Spacer(modifier = Modifier.width(12.dp))
                        Text(
                            text = selectedFileName ?: "",
                            color = Color.White,
                            fontSize = 13.sp,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.weight(1f)
                        )
                        TextButton(
                            onClick = {
                                selectedFileUri = null
                                selectedFileName = null
                                // Status handled by manager
                            }
                        ) {
                            Text("取消", color = Color(0xFF888888), fontSize = 12.sp)
                        }
                    }
                }
                
                Spacer(modifier = Modifier.height(8.dp))
            }
            
            // Transfer Progress
            if (transferProgress > 0) {
                LinearProgressIndicator(
                    progress = transferProgress / 100f,
                    modifier = Modifier
                        .fillMaxWidth()
                        .height(4.dp)
                        .clip(RoundedCornerShape(2.dp)),
                    color = Color(0xFF6C63FF),
                    trackColor = Color(0xFF333333),
                )
                Spacer(modifier = Modifier.height(8.dp))
            }
            
            // Status Text
            Text(
                text = statusText,
                color = Color.Gray,
                fontSize = 12.sp,
                modifier = Modifier.padding(bottom = 8.dp)
            )
            
            // Action Buttons Row
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                // Discovery Button
                Button(
                    onClick = {
                        if (isDiscovering) {
                            engine.stopDiscovery()
                        } else {
                            scope.launch(Dispatchers.IO) {
                                val portToAdvertise = if (receiverPort > 0) receiverPort else 42424
                                engine.startDiscovery(portToAdvertise)
                                // Discovery now always succeeds - USB localhost peer is auto-added
                                withContext(Dispatchers.Main) {
                                    Toast.makeText(context, "发现已启动（支持手动添加/USB连接）", Toast.LENGTH_SHORT).show()
                                }
                            }
                        }
                    },
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = if (isDiscovering) Color(0xFF4A4E69) else Color(0xFF6C63FF)
                    )
                ) {
                    Text(
                        text = if (isDiscovering) "停止" else "发现设备",
                        fontSize = 14.sp
                    )
                }
                
                // Add Manual Peer Button
                Button(
                    onClick = { showAddPeerDialog = true },
                    modifier = Modifier
                        .height(48.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = Color(0xFFFF9800)
                    )
                ) {
                    Text(text = "+IP", fontSize = 14.sp)
                }
                
                // Send Button (only enabled when file and peer are selected)
                Button(
                    onClick = {
                        if (selectedPeer == null) {
                            Toast.makeText(context, "请先选择目标设备", Toast.LENGTH_SHORT).show()
                        } else if (selectedFileUri == null) {
                            Toast.makeText(context, "请先选择文件", Toast.LENGTH_SHORT).show()
                        } else {
                            scope.launch {
                                transferManager.sendFile(
                                    selectedFileUri!!,
                                    selectedPeer!!,
                                    onComplete = { success, error ->
                                        scope.launch {
                                            if (success) {
                                                Toast.makeText(context, "发送成功", Toast.LENGTH_SHORT).show()
                                            } else {
                                                Toast.makeText(context, error ?: "发送失败", Toast.LENGTH_SHORT).show()
                                            }
                                        }
                                    }
                                )
                            }
                        }
                    },
                    modifier = Modifier
                        .weight(1f)
                        .height(48.dp),
                    shape = RoundedCornerShape(12.dp),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = Color(0xFF4CAF50)
                    ),
                    enabled = selectedFileUri != null && selectedPeer != null
                ) {
                    Icon(
                        imageVector = Icons.AutoMirrored.Filled.Send,
                        contentDescription = "发送",
                        modifier = Modifier.size(18.dp)
                    )
                    Spacer(modifier = Modifier.width(6.dp))
                    Text(text = "发送", fontSize = 14.sp)
                }
            }
        }
        
        // FAB for file picker
        FloatingActionButton(
            onClick = {
                filePickerLauncher.launch(arrayOf("*/*"))
            },
            modifier = Modifier
                .align(Alignment.BottomEnd)
                .padding(end = 16.dp, bottom = 80.dp),
            containerColor = Color(0xFF6C63FF)
        ) {
            Icon(
                imageVector = Icons.Default.Add,
                contentDescription = "选择文件",
                tint = Color.White
            )
        }
    }
    
    // Manual Peer Addition Dialog
    if (showAddPeerDialog) {
        AlertDialog(
            onDismissRequest = { showAddPeerDialog = false },
            containerColor = Color(0xFF16213E),
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
    onClick: () -> Unit
) {
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 4.dp)
            .clickable(onClick = onClick),
        shape = RoundedCornerShape(8.dp),
        colors = CardDefaults.cardColors(
            containerColor = if (isSelected) Color(0xFF4A4E69) else Color(0xFF1A1A2E)
        )
    ) {
        Column(
            modifier = Modifier.padding(12.dp)
        ) {
            Text(
                text = peer.deviceName,
                color = Color.White,
                fontWeight = FontWeight.Medium
            )
            Text(
                text = "${peer.ipAddress}:${peer.port}",
                color = Color.Gray,
                fontSize = 12.sp
            )
        }
    }
}
