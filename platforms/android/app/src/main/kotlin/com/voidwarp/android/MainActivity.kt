package com.voidwarp.android

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.voidwarp.android.core.DiscoveredPeer
import com.voidwarp.android.core.VoidWarpEngine
import com.voidwarp.android.ui.theme.VoidWarpTheme
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    
    private var engine: VoidWarpEngine? = null
    
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        
        engine = VoidWarpEngine(android.os.Build.MODEL)
        
        setContent {
            VoidWarpTheme {
                Surface(
                    modifier = Modifier.fillMaxSize(),
                    color = Color(0xFF1A1A2E)
                ) {
                    MainScreen(engine!!)
                }
            }
        }
    }
    
    override fun onDestroy() {
        engine?.close()
        super.onDestroy()
    }
}

@Composable
fun MainScreen(engine: VoidWarpEngine) {
    val isDiscovering by engine.isDiscovering.collectAsState()
    val peers by engine.peers.collectAsState()
    var selectedPeer by remember { mutableStateOf<DiscoveredPeer?>(null) }
    val scope = rememberCoroutineScope()
    
    // Auto-refresh peers
    LaunchedEffect(isDiscovering) {
        while (isDiscovering) {
            engine.refreshPeers()
            delay(1000)
        }
    }
    
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
        
        Spacer(modifier = Modifier.height(24.dp))
        
        // Discovery Status
        Row(
            verticalAlignment = Alignment.CenterVertically,
            modifier = Modifier.padding(bottom = 16.dp)
        ) {
            Box(
                modifier = Modifier
                    .size(12.dp)
                    .clip(CircleShape)
                    .background(if (isDiscovering) Color(0xFF6C63FF) else Color.Gray)
            )
            Spacer(modifier = Modifier.width(8.dp))
            Text(
                text = if (isDiscovering) "正在发现设备 (${peers.size})" else "发现已停止",
                color = Color.LightGray,
                fontSize = 14.sp
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
        
        Spacer(modifier = Modifier.height(16.dp))
        
        // Action Button
        Button(
            onClick = {
                if (isDiscovering) {
                    engine.stopDiscovery()
                } else {
                    engine.startDiscovery()
                }
            },
            modifier = Modifier
                .fillMaxWidth()
                .height(56.dp),
            shape = RoundedCornerShape(12.dp),
            colors = ButtonDefaults.buttonColors(
                containerColor = Color(0xFF6C63FF)
            )
        ) {
            Text(
                text = if (isDiscovering) "停止发现" else "开始发现设备",
                fontSize = 16.sp
            )
        }
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
