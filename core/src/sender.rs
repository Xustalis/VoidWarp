//! TCP File Sender Module
//!
//! Handles sending files over TCP with checksum verification, chunking,
//! acknowledgments, and resume support.

use crate::checksum::{calculate_chunk_checksum, calculate_file_checksum};
use std::fs::File;
use std::io::{BufReader, Read, Write};
use std::net::{SocketAddr, TcpStream};
use std::path::Path;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::Arc;
use std::time::Duration;

/// Default chunk size (1MB)
pub const DEFAULT_CHUNK_SIZE: usize = 1024 * 1024;

/// Connection timeout
const CONNECT_TIMEOUT: Duration = Duration::from_secs(10);

/// Read timeout for ACKs
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
}

impl TcpFileSender {
    /// Create a new file sender
    pub fn new(file_path: &str) -> std::io::Result<Self> {
        let path = Path::new(file_path);
        let metadata = path.metadata()?;
        let file_size = metadata.len();

        tracing::info!("Calculating checksum for file: {}", file_path);
        let file_checksum = calculate_file_checksum(path)?;
        tracing::info!("File checksum: {}", file_checksum);

        Ok(TcpFileSender {
            file_path: file_path.to_string(),
            file_size,
            file_checksum,
            chunk_size: DEFAULT_CHUNK_SIZE,
            bytes_sent: Arc::new(AtomicU64::new(0)),
            cancelled: Arc::new(AtomicBool::new(false)),
            resume_from_chunk: 0,
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
        if let Err(e) = self.send_handshake(&mut stream, sender_name) {
            return TransferResult::IoError(format!("Handshake failed: {}", e));
        }

        // Wait for accept/reject response
        let mut response = [0u8; 1];
        match stream.read_exact(&mut response) {
            Ok(_) => {
                if response[0] == 0 {
                    tracing::info!("Transfer rejected by receiver");
                    return TransferResult::Rejected;
                }
            }
            Err(e) => {
                return TransferResult::IoError(format!("Failed to read response: {}", e));
            }
        }

        tracing::info!("Transfer accepted, starting file transfer...");

        // If resuming, read the resume chunk index from receiver
        let start_chunk = if self.resume_from_chunk > 0 {
            self.resume_from_chunk
        } else {
            // Check if receiver wants to resume
            let mut resume_buf = [0u8; 8];
            if stream.read_exact(&mut resume_buf).is_ok() {
                u64::from_be_bytes(resume_buf)
            } else {
                0
            }
        };

        // Open file and seek to resume position
        let file = match File::open(&self.file_path) {
            Ok(f) => f,
            Err(e) => return TransferResult::IoError(e.to_string()),
        };
        let mut reader = BufReader::with_capacity(self.chunk_size, file);

        // Skip to resume position
        let start_offset = start_chunk * self.chunk_size as u64;
        if start_offset > 0 {
            let mut skip_buf = vec![0u8; std::cmp::min(start_offset as usize, 8 * 1024 * 1024)];
            let mut skipped: u64 = 0;
            while skipped < start_offset {
                let to_skip = std::cmp::min(skip_buf.len() as u64, start_offset - skipped) as usize;
                match reader.read(&mut skip_buf[..to_skip]) {
                    Ok(0) => break,
                    Ok(n) => skipped += n as u64,
                    Err(e) => return TransferResult::IoError(e.to_string()),
                }
            }
            self.bytes_sent.store(skipped, Ordering::SeqCst);
            tracing::info!(
                "Resuming from chunk {}, offset {}",
                start_chunk,
                start_offset
            );
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
                if let Err(e) =
                    self.send_chunk(&mut stream, chunk_index, chunk_data, &chunk_checksum)
                {
                    tracing::warn!("Failed to send chunk {}: {}", chunk_index, e);
                    retries += 1;
                    if retries >= MAX_RETRIES {
                        return TransferResult::IoError(format!("Max retries exceeded: {}", e));
                    }
                    continue;
                }

                // Wait for ACK
                match self.wait_for_ack(&mut stream, chunk_index) {
                    Ok(true) => break, // ACK received
                    Ok(false) => {
                        tracing::warn!("Chunk {} checksum failed, retransmitting", chunk_index);
                        retries += 1;
                        if retries >= MAX_RETRIES {
                            return TransferResult::ChecksumMismatch;
                        }
                    }
                    Err(e) => {
                        tracing::warn!("ACK timeout for chunk {}: {}", chunk_index, e);
                        retries += 1;
                        if retries >= MAX_RETRIES {
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
        let mut final_result = [0u8; 1];
        match stream.read_exact(&mut final_result) {
            Ok(_) => {
                if final_result[0] == 1 {
                    tracing::info!("Transfer completed successfully!");
                    TransferResult::Success
                } else {
                    tracing::error!("Final checksum verification failed");
                    TransferResult::ChecksumMismatch
                }
            }
            Err(e) => TransferResult::IoError(format!("Failed to read final result: {}", e)),
        }
    }

    /// Send handshake packet
    fn send_handshake(&self, stream: &mut TcpStream, sender_name: &str) -> std::io::Result<()> {
        // Protocol:
        // [sender_name_len: u8][sender_name][file_name_len: u16 BE][file_name]
        // [file_size: u64 BE][chunk_size: u32 BE][checksum_len: u8][checksum: 32 bytes hex]

        let sender_bytes = sender_name.as_bytes();
        let file_name = self.file_name();
        let file_name_bytes = file_name.as_bytes();
        let checksum_bytes = self.file_checksum.as_bytes();

        // Sender name
        stream.write_all(&[sender_bytes.len() as u8])?;
        stream.write_all(sender_bytes)?;

        // File name
        stream.write_all(&(file_name_bytes.len() as u16).to_be_bytes())?;
        stream.write_all(file_name_bytes)?;

        // File size
        stream.write_all(&self.file_size.to_be_bytes())?;

        // Chunk size (NEW)
        stream.write_all(&(self.chunk_size as u32).to_be_bytes())?;

        // Checksum
        stream.write_all(&[checksum_bytes.len() as u8])?;
        stream.write_all(checksum_bytes)?;

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
             return Err(std::io::Error::new(std::io::ErrorKind::InvalidData, "Invalid checksum length"));
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
