//! TCP File Sender Module
//!
//! Handles sending files over TCP with checksum verification, chunking,
//! acknowledgments, and resume support.

use crate::checksum::{calculate_chunk_checksum, calculate_file_checksum};
use crate::io_utils::MultiFileReader;
use crate::protocol::TransferType;
use std::fs::File;
use std::io::{BufReader, Read, Seek, Write};
use std::net::{SocketAddr, TcpStream};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;

/// Default chunk size (1MB)
pub const DEFAULT_CHUNK_SIZE: usize = 1024 * 1024;

/// Connection timeout
const CONNECT_TIMEOUT: Duration = Duration::from_secs(10);

/// Handshake timeout (waiting for user accept/reject)
/// This MUST be long enough for the user to manually accept/reject the transfer
const HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(60);

/// Read timeout for ACKs during data transfer
const ACK_TIMEOUT: Duration = Duration::from_secs(30);

/// Max retries per chunk
const MAX_RETRIES: u32 = 3;

/// Transfer result
#[derive(Debug, Clone)]
pub enum TransferResult {
    Success,
    Rejected,
    ChecksumMismatch,
    ConnectionFailed(String),
    Timeout,
    Cancelled,
    IoError(String),
}

/// File sender for TCP transfer
pub struct TcpFileSender {
    file_path: String,
    file_size: u64,
    file_checksum: String,
    chunk_size: usize,
    bytes_sent: Arc<AtomicU64>,
    cancelled: Arc<AtomicBool>,
    resume_from_chunk: u64,
    pub transfer_type: TransferType,
    manifest_bytes: Vec<u8>,
    files_to_send: Vec<PathBuf>,
}

impl TcpFileSender {
    /// Create a new file sender (handles both files and folders)
    pub fn new(path_str: &str) -> std::io::Result<Self> {
        let path = Path::new(path_str);
        if !path.exists() {
             return Err(std::io::Error::new(std::io::ErrorKind::NotFound, "Path not found"));
        }

        if path.is_dir() {
            Self::new_folder(path_str)
        } else {
            Self::new_single_file(path_str)
        }
    }

    /// Create a sender for a single file
    fn new_single_file(file_path: &str) -> std::io::Result<Self> {
        let path = Path::new(file_path);
        let metadata = path.metadata()?;
        let file_size = metadata.len();

        tracing::info!("Calculating checksum for file: {}", file_path);
        let file_checksum = calculate_file_checksum(path)?;
        tracing::info!("File checksum: {}", file_checksum);

        use crate::protocol::TransferType;

        Ok(TcpFileSender {
            file_path: file_path.to_string(), // Root path
            file_size,
            file_checksum,
            chunk_size: DEFAULT_CHUNK_SIZE,
            bytes_sent: Arc::new(AtomicU64::new(0)),
            cancelled: Arc::new(AtomicBool::new(false)),
            resume_from_chunk: 0,
            transfer_type: TransferType::SingleFile,
            manifest_bytes: vec![],
            files_to_send: vec![path.to_path_buf()],
        })
    }

    /// Create a sender for a folder
    fn new_folder(folder_path: &str) -> std::io::Result<Self> {
        use crate::protocol::{ManifestItem, TransferManifest, TransferType};
        use std::fs;

        let root_path = Path::new(folder_path);
        let mut items = Vec::new();
        let mut total_content_size = 0u64;
        let mut files_to_send = Vec::new();

        tracing::info!("Scanning folder: {}", folder_path);

        // Recursive scan
        // Use a stack for non-recursive iteration to avoid stack overflow on deepdirs
        let mut stack = vec![root_path.to_path_buf()];
        
        while let Some(path) = stack.pop() {
            if path.is_dir() {
                for entry in fs::read_dir(&path)? {
                    let entry = entry?;
                    stack.push(entry.path());
                }
            } else {
                let metadata = path.metadata()?;
                let size = metadata.len();
                
                // compute relative path
                let relative_path = path.strip_prefix(root_path)
                    .map_err(|e| std::io::Error::new(std::io::ErrorKind::InvalidData, e))?
                    .to_string_lossy()
                    .replace("\\", "/"); // standardize on forward slash
                
                // Compute hash (This might take time for large folders!)
                // TODO: Optimize? Maybe compute lazily? Protocol requires hash upfront for Manifest...
                // V2 Protocol requires Manifest Hash in Handshake. Manifest contains File hashes.
                // So we MUST compute all file hashes now.
                tracing::debug!("Hashing file: {}", relative_path);
                let hash = calculate_file_checksum(&path)?;
                
                items.push(ManifestItem {
                    path: relative_path,
                    size,
                    hash,
                });
                
                total_content_size += size;
                files_to_send.push(path);
            }
        }
        
        // Create Manifest
        let manifest = TransferManifest {
            items,
            total_size: total_content_size,
        };
        
        let manifest_json = serde_json::to_string(&manifest)?;
        let manifest_bytes = manifest_json.into_bytes();
        // Pack length (4 bytes u32 big endian) + JSON bytes
        let mut full_manifest_data = Vec::new();
        full_manifest_data.extend_from_slice(&(manifest_bytes.len() as u32).to_be_bytes());
        full_manifest_data.extend_from_slice(&manifest_bytes);
        
        // Calculate checksum of the MANIFEST (not the files, files are hashed inside manifest)
        let manifest_hash = crate::checksum::calculate_chunk_checksum(&manifest_bytes);
        
        let total_transfer_size = (full_manifest_data.len() as u64) + total_content_size;
        
        tracing::info!("Folder scan complete. {} files, total size: {}", files_to_send.len(), total_transfer_size);

        Ok(TcpFileSender {
            file_path: folder_path.to_string(),
            file_size: total_transfer_size,
            file_checksum: manifest_hash,
            chunk_size: DEFAULT_CHUNK_SIZE,
            bytes_sent: Arc::new(AtomicU64::new(0)),
            cancelled: Arc::new(AtomicBool::new(false)),
            resume_from_chunk: 0,
            transfer_type: TransferType::Folder,
            manifest_bytes: full_manifest_data,
            files_to_send,
        })
    }

    /// Set chunk size
    pub fn set_chunk_size(&mut self, size: usize) {
        self.chunk_size = size;
    }

    /// Set resume point (chunk index to start from)
    pub fn set_resume_from(&mut self, chunk_index: u64) {
        self.resume_from_chunk = chunk_index;
    }

    /// Get bytes sent so far
    pub fn bytes_sent(&self) -> u64 {
        self.bytes_sent.load(Ordering::SeqCst)
    }

    /// Get progress (0-100)
    pub fn progress(&self) -> f32 {
        if self.file_size == 0 {
            return 100.0;
        }
        (self.bytes_sent() as f32 / self.file_size as f32) * 100.0
    }

    /// Cancel the transfer
    pub fn cancel(&self) {
        self.cancelled.store(true, Ordering::SeqCst);
    }

    /// Get file size
    pub fn file_size(&self) -> u64 {
        self.file_size
    }

    /// Get file checksum
    pub fn checksum(&self) -> &str {
        &self.file_checksum
    }

    /// Get file name
    pub fn file_name(&self) -> String {
        Path::new(&self.file_path)
            .file_name()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_else(|| "unknown".to_string())
    }

    /// Test connection to a peer
    pub fn test_connection(peer_addr: SocketAddr) -> TransferResult {
        tracing::info!("Testing connection to {}...", peer_addr);
        match TcpStream::connect_timeout(&peer_addr, CONNECT_TIMEOUT) {
            Ok(_) => {
                tracing::info!("Connection test successful!");
                // We established a TCP connection. That's enough to prove reachability.
                // We purposefully don't send any data to avoid confusing the receiver
                // which expects a handshake. The receiver will just see a connect/disconnect.
                TransferResult::Success
            }
            Err(e) => {
                tracing::error!("Connection test failed: {}", e);
                TransferResult::ConnectionFailed(e.to_string())
            }
        }
    }

    /// Send file to a peer
    pub fn send_to(&self, peer_addr: SocketAddr, sender_name: &str) -> TransferResult {
        tracing::info!("Connecting to {} for file transfer...", peer_addr);

        // Connect with timeout
        let stream = match TcpStream::connect_timeout(&peer_addr, CONNECT_TIMEOUT) {
            Ok(s) => s,
            Err(e) => {
                tracing::error!("Failed to connect: {}", e);
                return TransferResult::ConnectionFailed(e.to_string());
            }
        };

        if let Err(e) = stream.set_read_timeout(Some(ACK_TIMEOUT)) {
            tracing::warn!("Failed to set read timeout: {}", e);
        }
        if let Err(e) = stream.set_write_timeout(Some(ACK_TIMEOUT)) {
            tracing::warn!("Failed to set write timeout: {}", e);
        }

        self.send_over_stream(stream, sender_name)
    }

    /// Send file over an established stream
    fn send_over_stream(&self, mut stream: TcpStream, sender_name: &str) -> TransferResult {
        // Send handshake
        tracing::info!("Sending file offer handshake to receiver...");
        if let Err(e) = self.send_handshake(&mut stream, sender_name) {
            tracing::error!("Failed to send handshake: {}", e);
            return TransferResult::IoError(format!("Handshake failed: {}", e));
        }
        tracing::info!("Handshake sent successfully, waiting for user acceptance...");

        // CRITICAL: Set longer timeout for user acceptance
        // User needs time to manually accept/reject the transfer
        if let Err(e) = stream.set_read_timeout(Some(HANDSHAKE_TIMEOUT)) {
            tracing::warn!("Failed to set handshake timeout: {}", e);
        }

        // Wait for accept/reject response
        let mut response = [0u8; 1];
        match stream.read_exact(&mut response) {
            Ok(_) => {
                if response[0] == 0 {
                    tracing::info!("Transfer rejected by receiver");
                    return TransferResult::Rejected;
                } else {
                    tracing::info!("Transfer accepted by receiver!");
                }
            }
            Err(e) => {
                tracing::error!(
                    "Failed to read accept/reject response: {} (timeout: {:?})",
                    e,
                    HANDSHAKE_TIMEOUT
                );
                if e.kind() == std::io::ErrorKind::WouldBlock
                    || e.kind() == std::io::ErrorKind::TimedOut
                {
                    return TransferResult::Timeout;
                }
                return TransferResult::IoError(format!("Failed to read response: {}", e));
            }
        }

        // Reset timeout to ACK_TIMEOUT for data transfer
        if let Err(e) = stream.set_read_timeout(Some(ACK_TIMEOUT)) {
            tracing::warn!("Failed to set ACK timeout: {}", e);
        }

        tracing::info!("Transfer accepted, starting file transfer...");

        // If resuming, read the resume chunk index from receiver
        let start_chunk = if self.resume_from_chunk > 0 {
            tracing::info!(
                "Resuming from chunk {} (requested by sender)",
                self.resume_from_chunk
            );
            self.resume_from_chunk
        } else {
            // Check if receiver wants to resume
            let mut resume_buf = [0u8; 8];
            match stream.read_exact(&mut resume_buf) {
                Ok(_) => {
                    let chunk = u64::from_be_bytes(resume_buf);
                    if chunk > 0 {
                        tracing::info!("Receiver requested resume from chunk {}", chunk);
                    } else {
                        tracing::info!("Starting fresh transfer from chunk 0");
                    }
                    chunk
                }
                Err(e) => {
                    tracing::warn!("Failed to read resume chunk index: {}, starting from 0", e);
                    0
                }
            }
        };

        // Create MultiFileReader
        let mut reader = match MultiFileReader::new(self.manifest_bytes.clone(), self.files_to_send.clone()) {
            Ok(r) => r,
            Err(e) => return TransferResult::IoError(e.to_string()),
        };

        // Skip to resume position
        let start_offset = start_chunk * self.chunk_size as u64;
        if start_offset > 0 {
            tracing::info!(
                "Resuming from chunk {}, offset {}",
                start_chunk,
                start_offset
            );
            
            if let Err(e) = reader.seek(std::io::SeekFrom::Start(start_offset)) {
                 return TransferResult::IoError(format!("Failed to seek to resume position: {}", e));
            }
            
            self.bytes_sent.store(start_offset, Ordering::SeqCst);
        }

        // Send file in chunks
        let mut chunk_buffer = vec![0u8; self.chunk_size];
        let mut chunk_index = start_chunk;

        loop {
            if self.cancelled.load(Ordering::SeqCst) {
                tracing::info!("Transfer cancelled");
                return TransferResult::Cancelled;
            }

            let bytes_read = match reader.read(&mut chunk_buffer) {
                Ok(0) => break, // EOF
                Ok(n) => n,
                Err(e) => return TransferResult::IoError(e.to_string()),
            };

            let chunk_data = &chunk_buffer[..bytes_read];
            let chunk_checksum = calculate_chunk_checksum(chunk_data);

            // Send chunk with retries
            let mut retries = 0;
            loop {
                if retries == 0 {
                    tracing::debug!("Sending chunk {} ({} bytes)", chunk_index, bytes_read);
                } else {
                    tracing::warn!(
                        "Retrying chunk {} (attempt {}/{})",
                        chunk_index,
                        retries + 1,
                        MAX_RETRIES
                    );
                }

                if let Err(e) =
                    self.send_chunk(&mut stream, chunk_index, chunk_data, &chunk_checksum)
                {
                    tracing::error!("Failed to send chunk {}: {}", chunk_index, e);
                    retries += 1;
                    if retries >= MAX_RETRIES {
                        tracing::error!("Max retries exceeded for chunk {}", chunk_index);
                        return TransferResult::IoError(format!("Max retries exceeded: {}", e));
                    }
                    continue;
                }

                // Wait for ACK
                tracing::trace!("Waiting for ACK for chunk {}...", chunk_index);
                match self.wait_for_ack(&mut stream, chunk_index) {
                    Ok(true) => {
                        tracing::trace!("Received ACK for chunk {}", chunk_index);
                        break; // ACK received
                    }
                    Ok(false) => {
                        tracing::warn!(
                            "Chunk {} checksum verification failed on receiver, retransmitting",
                            chunk_index
                        );
                        retries += 1;
                        if retries >= MAX_RETRIES {
                            tracing::error!(
                                "Max retries exceeded due to checksum mismatch for chunk {}",
                                chunk_index
                            );
                            return TransferResult::ChecksumMismatch;
                        }
                    }
                    Err(e) => {
                        tracing::error!("Timeout waiting for ACK for chunk {}: {}", chunk_index, e);
                        retries += 1;
                        if retries >= MAX_RETRIES {
                            tracing::error!(
                                "Max retries exceeded due to ACK timeout for chunk {}",
                                chunk_index
                            );
                            return TransferResult::Timeout;
                        }
                    }
                }
            }

            self.bytes_sent
                .fetch_add(bytes_read as u64, Ordering::SeqCst);
            chunk_index += 1;

            if chunk_index % 100 == 0 {
                tracing::debug!("Sent {} chunks, {:.1}%", chunk_index, self.progress());
            }
        }

        // Wait for final verification
        tracing::info!("All chunks sent, waiting for final verification from receiver...");
        let mut final_result = [0u8; 1];
        match stream.read_exact(&mut final_result) {
            Ok(_) => {
                if final_result[0] == 1 {
                    tracing::info!("✓ Transfer completed successfully! Final checksum verified.");
                    TransferResult::Success
                } else {
                    tracing::error!("✗ Final checksum verification failed on receiver");
                    TransferResult::ChecksumMismatch
                }
            }
            Err(e) => {
                tracing::error!("Failed to read final verification result: {}", e);
                TransferResult::IoError(format!("Failed to read final result: {}", e))
            }
        }
    }

    /// Send handshake packet
    fn send_handshake(&self, stream: &mut TcpStream, sender_name: &str) -> std::io::Result<()> {
        use crate::protocol::{HandshakeRequest, TransferType};

        let request = HandshakeRequest::new(
            sender_name,
            &self.file_name(),
            self.file_size,
            self.chunk_size as u32,
            &self.file_checksum,
            self.transfer_type,
        );

        request.write_to(stream)?;
        stream.flush()?;
        Ok(())
    }

    /// Send a chunk
    fn send_chunk(
        &self,
        stream: &mut TcpStream,
        index: u64,
        data: &[u8],
        checksum: &str,
    ) -> std::io::Result<()> {
        // [chunk_index: u64 BE][chunk_len: u32 BE][data][checksum: 16 bytes md5 binary]
        stream.write_all(&index.to_be_bytes())?;
        stream.write_all(&(data.len() as u32).to_be_bytes())?;
        stream.write_all(data)?;

        // Convert hex checksum to bytes (full 16 bytes/128 bits)
        let checksum_bytes: Vec<u8> = (0..checksum.len())
            .step_by(2)
            .filter_map(|i| u8::from_str_radix(&checksum[i..i + 2], 16).ok())
            .collect();

        // Ensure we send exactly 16 bytes
        if checksum_bytes.len() != 16 {
            return Err(std::io::Error::new(
                std::io::ErrorKind::InvalidData,
                "Invalid checksum length",
            ));
        }

        stream.write_all(&checksum_bytes)?;

        stream.flush()?;
        Ok(())
    }

    /// Wait for chunk ACK
    fn wait_for_ack(&self, stream: &mut TcpStream, expected_index: u64) -> std::io::Result<bool> {
        // [acked_chunk_index: u64 BE][status: u8] where 0=ok, 1=checksum_fail
        let mut ack_buf = [0u8; 9];
        stream.read_exact(&mut ack_buf)?;

        let acked_index = u64::from_be_bytes(ack_buf[0..8].try_into().unwrap());
        let status = ack_buf[8];

        if acked_index != expected_index {
            tracing::warn!(
                "ACK index mismatch: expected {}, got {}",
                expected_index,
                acked_index
            );
            return Ok(false);
        }

        Ok(status == 0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;
    use tempfile::NamedTempFile;

    #[test]
    fn test_sender_creation() {
        let mut temp = NamedTempFile::new().unwrap();
        temp.write_all(b"Test content for sender").unwrap();
        temp.flush().unwrap();

        let sender = TcpFileSender::new(temp.path().to_str().unwrap()).unwrap();
        assert!(sender.file_size() > 0);
        assert!(!sender.checksum().is_empty());
    }
}
