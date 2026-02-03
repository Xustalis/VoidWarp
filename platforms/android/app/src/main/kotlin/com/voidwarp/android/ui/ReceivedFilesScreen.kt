package com.voidwarp.android.ui

import android.content.Intent
import android.net.Uri
import android.os.Environment
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.content.FileProvider
import java.io.File
import java.text.SimpleDateFormat
import java.util.*

import androidx.activity.compose.BackHandler
import com.voidwarp.android.core.HistoryManager
import com.voidwarp.android.core.HistoryItem
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material3.Checkbox
import androidx.compose.ui.text.style.TextDecoration

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ReceivedFilesScreen(
    historyManager: HistoryManager,
    onBack: () -> Unit
) {
    val context = LocalContext.current
    val items by historyManager.items.collectAsState()
    
    // Deletion State
    var itemToDelete by remember { mutableStateOf<HistoryItem?>(null) }
    var showDeleteDialog by remember { mutableStateOf(false) }
    var deleteFileChecked by remember { mutableStateOf(false) }
    
    // Handle system back button
    BackHandler {
        onBack()
    }
    
    // Delete Confirmation Dialog
    if (showDeleteDialog && itemToDelete != null) {
        AlertDialog(
            onDismissRequest = { showDeleteDialog = false },
            containerColor = com.voidwarp.android.AppColors.Surface,
            title = { Text("删除记录", color = Color.White) },
            text = {
                Column {
                    Text("确定要删除此传输记录吗？", color = Color.Gray)
                    Spacer(modifier = Modifier.height(16.dp))
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        modifier = Modifier.clickable { deleteFileChecked = !deleteFileChecked }
                    ) {
                        Checkbox(
                            checked = deleteFileChecked,
                            onCheckedChange = { deleteFileChecked = it },
                            colors = CheckboxDefaults.colors(
                                checkedColor = com.voidwarp.android.AppColors.Primary,
                                uncheckedColor = Color.Gray,
                                checkmarkColor = Color.White
                            )
                        )
                        Text("同时删除本地文件", color = Color.White, modifier = Modifier.padding(start = 8.dp))
                    }
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        historyManager.delete(itemToDelete!!, deleteFileChecked)
                        showDeleteDialog = false
                        itemToDelete = null
                    },
                    colors = ButtonDefaults.buttonColors(containerColor = com.voidwarp.android.AppColors.Error)
                ) { Text("删除") }
            },
            dismissButton = {
                TextButton(onClick = { showDeleteDialog = false }) { Text("取消", color = Color.Gray) }
            }
        )
    }
    
    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(com.voidwarp.android.AppColors.Background)
            .padding(16.dp)
    ) {
        // Header
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically
        ) {
            IconButton(onClick = onBack) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "返回", tint = Color.White)
            }
            Text(
                text = "接收记录",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = Color.White,
                modifier = Modifier.weight(1f)
            )
        }
        
        Spacer(modifier = Modifier.height(16.dp))
        
        // List
        if (items.isEmpty()) {
            Box(
                modifier = Modifier.fillMaxSize(),
                contentAlignment = Alignment.Center
            ) {
                Text(
                    text = "暂无接收记录",
                    color = Color.Gray,
                    textAlign = androidx.compose.ui.text.style.TextAlign.Center
                )
            }
        } else {
            LazyColumn(
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                items(items) { item ->
                    HistoryItemRow(
                        item = item,
                        context = context,
                        onDelete = {
                            itemToDelete = item
                            deleteFileChecked = false // Reset default
                            showDeleteDialog = true
                        }
                    )
                }
            }
        }
    }
}

@Composable
fun HistoryItemRow(
    item: HistoryItem,
    context: android.content.Context,
    onDelete: () -> Unit
) {
    val dateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm", Locale.getDefault())
    val fileExists = item.fileExists
    
    Card(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(enabled = fileExists) {
                try {
                    val file = File(item.filePath)
                    val uri = FileProvider.getUriForFile(
                        context,
                        "${context.packageName}.provider",
                        file
                    )
                    val intent = Intent(Intent.ACTION_VIEW).apply {
                        setDataAndType(uri, "*/*")
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                    }
                    context.startActivity(intent)
                } catch (e: Exception) {
                    android.widget.Toast.makeText(context, "无法打开文件: ${e.message}", android.widget.Toast.LENGTH_SHORT).show()
                }
            },
        shape = RoundedCornerShape(8.dp),
        colors = CardDefaults.cardColors(containerColor = com.voidwarp.android.AppColors.SurfaceVariant)
    ) {
        Row(
            modifier = Modifier.padding(12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Icon
            Icon(
                Icons.Default.Description,
                contentDescription = null,
                tint = if (fileExists) com.voidwarp.android.AppColors.Primary else Color.Gray,
                modifier = Modifier.size(32.dp)
            )
            
            Spacer(modifier = Modifier.width(12.dp))
            
            // Info
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = item.fileName,
                    color = if (fileExists) Color.White else Color.Gray,
                    fontWeight = FontWeight.Medium,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    style = if (!fileExists) androidx.compose.ui.text.TextStyle(textDecoration = TextDecoration.LineThrough) else androidx.compose.ui.text.TextStyle()
                )
                Text(
                    text = "${item.formattedSize} • 来自: ${item.senderName}",
                    color = Color.Gray,
                    fontSize = 12.sp
                )
                Text(
                    text = dateFormat.format(Date(item.receivedTime)),
                    color = Color.DarkGray,
                    fontSize = 10.sp
                )
            }
            
            // Delete Button
            IconButton(onClick = onDelete) {
                Icon(
                    Icons.Default.Delete,
                    contentDescription = "删除",
                    tint = com.voidwarp.android.AppColors.Error,
                    modifier = Modifier.size(20.dp)
                )
            }
        }
    }
}
fun formatFileSize(size: Long): String {
    return when {
        size >= 1024 * 1024 * 1024 -> "%.2f GB".format(size / 1024.0 / 1024.0 / 1024.0)
        size >= 1024 * 1024 -> "%.1f MB".format(size / 1024.0 / 1024.0)
        size >= 1024 -> "%.1f KB".format(size / 1024.0)
        else -> "$size 字节"
    }
}
