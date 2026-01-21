//! File Transfer Module
//!
//! Handles chunked file transfer with progress tracking.

use std::fs::File;
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::Path;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;

/// Default chunk size: 1MB
pub const DEFAULT_CHUNK_SIZE: usize = 1024 * 1024;

/// Transfer state
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum TransferState {
    Pending,
    Transferring,
    Paused,
    Completed,
    Failed,
    Cancelled,
}

/// File transfer metadata
#[derive(Debug, Clone)]
pub struct FileMetadata {
    pub name: String,
    pub size: u64,
    pub chunk_size: usize,
    pub total_chunks: u64,
}

impl FileMetadata {
    pub fn from_path(path: &Path) -> std::io::Result<Self> {
        let metadata = std::fs::metadata(path)?;
        let name = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| "unknown".to_string());
        let size = metadata.len();
        let chunk_size = DEFAULT_CHUNK_SIZE;
        let total_chunks = size.div_ceil(chunk_size as u64);

        Ok(FileMetadata {
            name,
            size,
            chunk_size,
            total_chunks,
        })
    }
}

/// Progress information for callbacks
#[derive(Debug, Clone)]
pub struct TransferProgress {
    pub bytes_transferred: u64,
    pub total_bytes: u64,
    pub chunks_completed: u64,
    pub total_chunks: u64,
    pub speed_bytes_per_sec: u64,
    pub state: TransferState,
}

impl TransferProgress {
    pub fn percentage(&self) -> f32 {
        if self.total_bytes == 0 {
            return 0.0;
        }
        (self.bytes_transferred as f32 / self.total_bytes as f32) * 100.0
    }
}

/// File sender - reads file in chunks
pub struct FileSender {
    file: File,
    metadata: FileMetadata,
    current_chunk: u64,
    bytes_sent: Arc<AtomicU64>,
    cancelled: Arc<AtomicBool>,
}

impl FileSender {
    pub fn new(path: &Path) -> std::io::Result<Self> {
        let file = File::open(path)?;
        let metadata = FileMetadata::from_path(path)?;

        Ok(FileSender {
            file,
            metadata,
            current_chunk: 0,
            bytes_sent: Arc::new(AtomicU64::new(0)),
            cancelled: Arc::new(AtomicBool::new(false)),
        })
    }

    pub fn metadata(&self) -> &FileMetadata {
        &self.metadata
    }

    pub fn cancel(&self) {
        self.cancelled.store(true, Ordering::SeqCst);
    }

    pub fn is_cancelled(&self) -> bool {
        self.cancelled.load(Ordering::SeqCst)
    }

    /// Read the next chunk
    pub fn read_chunk(&mut self) -> std::io::Result<Option<(u64, Vec<u8>)>> {
        if self.is_cancelled() {
            return Ok(None);
        }

        if self.current_chunk >= self.metadata.total_chunks {
            return Ok(None);
        }

        let offset = self.current_chunk * self.metadata.chunk_size as u64;
        self.file.seek(SeekFrom::Start(offset))?;

        let mut buffer = vec![0u8; self.metadata.chunk_size];
        let bytes_read = self.file.read(&mut buffer)?;

        if bytes_read == 0 {
            return Ok(None);
        }

        buffer.truncate(bytes_read);
        let chunk_index = self.current_chunk;
        self.current_chunk += 1;
        self.bytes_sent
            .fetch_add(bytes_read as u64, Ordering::SeqCst);

        Ok(Some((chunk_index, buffer)))
    }

    pub fn get_progress(&self) -> TransferProgress {
        let bytes = self.bytes_sent.load(Ordering::SeqCst);
        TransferProgress {
            bytes_transferred: bytes,
            total_bytes: self.metadata.size,
            chunks_completed: self.current_chunk,
            total_chunks: self.metadata.total_chunks,
            speed_bytes_per_sec: 0, // Calculated externally
            state: if self.is_cancelled() {
                TransferState::Cancelled
            } else if bytes >= self.metadata.size {
                TransferState::Completed
            } else {
                TransferState::Transferring
            },
        }
    }
}

/// File receiver - writes chunks to disk
pub struct FileReceiver {
    file: File,
    metadata: FileMetadata,
    bytes_received: Arc<AtomicU64>,
    chunks_received: u64,
}

impl FileReceiver {
    pub fn new(path: &Path, metadata: FileMetadata) -> std::io::Result<Self> {
        let file = File::create(path)?;

        Ok(FileReceiver {
            file,
            metadata,
            bytes_received: Arc::new(AtomicU64::new(0)),
            chunks_received: 0,
        })
    }

    /// Write a chunk at the specified index
    pub fn write_chunk(&mut self, chunk_index: u64, data: &[u8]) -> std::io::Result<()> {
        let offset = chunk_index * self.metadata.chunk_size as u64;
        self.file.seek(SeekFrom::Start(offset))?;
        self.file.write_all(data)?;
        self.bytes_received
            .fetch_add(data.len() as u64, Ordering::SeqCst);
        self.chunks_received += 1;
        Ok(())
    }

    pub fn finalize(&mut self) -> std::io::Result<()> {
        self.file.flush()?;
        // Truncate to exact size
        self.file.set_len(self.metadata.size)?;
        Ok(())
    }

    pub fn get_progress(&self) -> TransferProgress {
        let bytes = self.bytes_received.load(Ordering::SeqCst);
        TransferProgress {
            bytes_transferred: bytes,
            total_bytes: self.metadata.size,
            chunks_completed: self.chunks_received,
            total_chunks: self.metadata.total_chunks,
            speed_bytes_per_sec: 0,
            state: if bytes >= self.metadata.size {
                TransferState::Completed
            } else {
                TransferState::Transferring
            },
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;
    use tempfile::NamedTempFile;

    #[test]
    fn test_file_sender() {
        let mut temp = NamedTempFile::new().unwrap();
        let data = vec![0x42u8; 2 * DEFAULT_CHUNK_SIZE + 100];
        temp.write_all(&data).unwrap();
        temp.flush().unwrap();

        let mut sender = FileSender::new(temp.path()).unwrap();
        assert_eq!(sender.metadata().total_chunks, 3);

        let (idx, chunk) = sender.read_chunk().unwrap().unwrap();
        assert_eq!(idx, 0);
        assert_eq!(chunk.len(), DEFAULT_CHUNK_SIZE);
    }
}
