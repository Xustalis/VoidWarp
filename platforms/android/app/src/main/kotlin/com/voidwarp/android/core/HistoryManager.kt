package com.voidwarp.android.core

import android.content.Context
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import java.io.File
import java.util.UUID

data class HistoryItem(
    val id: String = UUID.randomUUID().toString(),
    val fileName: String,
    val filePath: String,
    val fileSize: Long,
    val senderName: String,
    val receivedTime: Long
) {
    val fileExists: Boolean get() = File(filePath).exists()
    
    val formattedSize: String
        get() = when {
            fileSize >= 1024 * 1024 * 1024 -> "%.2f GB".format(fileSize / 1024.0 / 1024.0 / 1024.0)
            fileSize >= 1024 * 1024 -> "%.1f MB".format(fileSize / 1024.0 / 1024.0)
            fileSize >= 1024 -> "%.1f KB".format(fileSize / 1024.0)
            else -> "$fileSize 字节"
        }
}

class HistoryManager(private val context: Context) {
    private val historyFile = File(context.filesDir, "transfer_history.json")
    private val gson = Gson()
    
    private val _items = mutableListOf<HistoryItem>()
    
    private val _itemsFlow = MutableStateFlow<List<HistoryItem>>(emptyList())
    val items: StateFlow<List<HistoryItem>> = _itemsFlow.asStateFlow()
    
    init {
        load()
    }
    
    fun getItems(): List<HistoryItem> {
        return _items.sortedByDescending { it.receivedTime }
    }
    
    fun add(item: HistoryItem) {
        _items.add(0, item)
        save()
        updateFlow()
    }
    
    fun delete(item: HistoryItem, deleteFile: Boolean) {
        _items.remove(item)
        
        if (deleteFile) {
            val file = File(item.filePath)
            if (file.exists()) {
                file.delete()
            }
        }
        
        save()
        updateFlow()
    }
    
    private fun updateFlow() {
        _itemsFlow.value = _items.toList()
    }
    
    private fun save() {
        try {
            val json = gson.toJson(_items)
            historyFile.writeText(json)
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }
    
    private fun load() {
        if (!historyFile.exists()) return
        
        try {
            val json = historyFile.readText()
            val type = object : TypeToken<List<HistoryItem>>() {}.type
            val loaded: List<HistoryItem>? = gson.fromJson(json, type)
            if (loaded != null) {
                _items.clear()
                _items.addAll(loaded)
                updateFlow()
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }
}
