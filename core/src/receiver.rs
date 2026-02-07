//! File Receiver Module
//!
//! TCP-based file receiver for accepting incoming file transfers.

use std::fs::File;
use std::io::{Read, Seek, Write};
use std::net::{SocketAddr, TcpListener, TcpStream};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;

use crate::checksum::{calculate_chunk_checksum, calculate_file_checksum};
use std::time::Duration;

// Timeouts
// Handshake timeout - should be long enough to receive the offer from sender
// The sender has 60s to send the offer after connecting
const HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(60);

// Data timeout - for receiving chunks during active transfer
const DATA_TIMEOUT: Duration = Duration::from_secs(30);

use crate::protocol::TransferType;

/// Incoming transfer request information
#[derive(Debug, Clone)]
pub struct IncomingTransfer {
    pub sender_name: String,
    pub sender_addr: SocketAddr,
    pub file_name: String,
    pub file_size: u64,
    pub chunk_size: u32,
    pub file_checksum: String, // Hex string
    pub transfer_type: TransferType,
}

/// Receiver state
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ReceiverState {
    Idle,
    Listening,
    AwaitingAccept,
    Receiving,
    Completed,
    Error,
}

/// File receiver that listens for incoming transfers
pub struct FileReceiverServer {
    listener: Option<TcpListener>,
    port: u16,
    running: Arc<AtomicBool>,
    state: Arc<Mutex<ReceiverState>>,
    pending_transfer: Arc<Mutex<Option<IncomingTransfer>>>,
    pending_stream: Arc<Mutex<Option<TcpStream>>>,
    bytes_received: Arc<AtomicU64>,
    total_bytes: Arc<AtomicU64>,
}

impl FileReceiverServer {
    /// Create a new file receiver server
    /// Tries to bind to port 42424 first (for ADB port forwarding compatibility),
    /// falls back to a random available port if 42424 is in use.
    pub fn new() -> std::io::Result<Self> {
        // Try to bind to the standard port first (for USB/ADB compatibility)
        // Try to bind to a range of standard ports (42424-42434)
        let mut listener_option = None;
        let mut port = 0;

        for p in 42424..42435 {
            match TcpListener::bind(format!("0.0.0.0:{}", p)) {
                Ok(l) => {
                    tracing::info!("FileReceiverServer bound to port {}", p);
                    listener_option = Some(l);
                    port = p;
                    break;
                }
                Err(_) => continue,
            }
        }

        let listener = match listener_option {
            Some(l) => l,
            None => {
                // Fallback to random if all fixed ports failed
                tracing::warn!("All standard ports (42424-42434) in use, falling back to random");
                let l = TcpListener::bind("0.0.0.0:0")?;
                port = l.local_addr()?.port();
                l
            }
        };

        // Set non-blocking for the listener
        listener.set_nonblocking(true)?;

        tracing::info!("FileReceiverServer created on port {}", port);

        Ok(FileReceiverServer {
            listener: Some(listener),
            port,
            running: Arc::new(AtomicBool::new(false)),
            state: Arc::new(Mutex::new(ReceiverState::Idle)),
            pending_transfer: Arc::new(Mutex::new(None)),
            pending_stream: Arc::new(Mutex::new(None)),
            bytes_received: Arc::new(AtomicU64::new(0)),
            total_bytes: Arc::new(AtomicU64::new(0)),
        })
    }

    /// Get the port the receiver is listening on
    pub fn port(&self) -> u16 {
        self.port
    }

    /// Get the current receiver state
    pub fn state(&self) -> ReceiverState {
        *self.state.lock().unwrap()
    }

    /// Get pending transfer info if any
    pub fn pending_transfer(&self) -> Option<IncomingTransfer> {
        self.pending_transfer.lock().unwrap().clone()
    }

    /// Start listening for incoming connections
    pub fn start(&self) {
        if self.running.load(Ordering::SeqCst) {
            return;
        }

        self.running.store(true, Ordering::SeqCst);
        *self.state.lock().unwrap() = ReceiverState::Listening;

        let listener = self.listener.as_ref().unwrap().try_clone().unwrap();
        let running = self.running.clone();
        let state = self.state.clone();
        let pending_transfer = self.pending_transfer.clone();
        let pending_stream = self.pending_stream.clone();

        thread::spawn(move || {
            tracing::info!("Receiver thread started, listening for incoming transfers...");

            while running.load(Ordering::SeqCst) {
                match listener.accept() {
                    Ok((mut stream, addr)) => {
                        tracing::info!("✓ Incoming connection from {}", addr);

                        // Set handshake timeouts (long enough to receive the offer)
                        if let Err(e) = stream.set_read_timeout(Some(HANDSHAKE_TIMEOUT)) {
                            tracing::warn!("Failed to set read timeout: {}", e);
                        }
                        if let Err(e) = stream.set_write_timeout(Some(HANDSHAKE_TIMEOUT)) {
                            tracing::warn!("Failed to set write timeout: {}", e);
                        }

                        // Read the file transfer handshake using the shared protocol
                        use crate::protocol::HandshakeRequest;

                        tracing::info!("Reading file offer handshake from sender...");
                        let handshake = match HandshakeRequest::read_from(&mut stream) {
                            Ok(h) => {
                                tracing::info!("Handshake received successfully");
                                h
                            }
                            Err(e) => {
                                tracing::error!("Failed to read handshake: {}", e);
                                // Try to send error byte if possible
                                let _ = stream.write_all(&[5u8]); // 5 = Reject/Error
                                let _ = stream.flush();
                                continue;
                            }
                        };

                        tracing::info!(
                            "📥 Incoming transfer offer:\n  File: '{}'\n  Size: {} bytes\n  From: '{}'\n  Address: {}",
                            handshake.file_name,
                            handshake.file_size,
                            handshake.sender_name,
                            addr
                        );

                        // Store pending transfer info
                        let transfer = IncomingTransfer {
                            sender_name: handshake.sender_name,
                            sender_addr: addr,
                            file_name: handshake.file_name,
                            file_size: handshake.file_size,
                            chunk_size: handshake.chunk_size,
                            file_checksum: handshake.file_checksum,
                            transfer_type: handshake.transfer_type,
                        };

                        *pending_transfer.lock().unwrap() = Some(transfer);
                        *pending_stream.lock().unwrap() = Some(stream);
                        *state.lock().unwrap() = ReceiverState::AwaitingAccept;

                        tracing::info!("⏳ Awaiting user acceptance/rejection...");

                        // Wait for accept/reject (handled by accept_transfer/reject_transfer)
                        break;
                    }
                    Err(ref e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                        // No connection yet, sleep briefly
                        thread::sleep(std::time::Duration::from_millis(100));
                    }
                    Err(e) => {
                        tracing::error!("Accept error: {}", e);
                        break;
                    }
                }
            }

            tracing::info!("Receiver thread ended");
        });
    }

    /// Stop listening
    pub fn stop(&self) {
        self.running.store(false, Ordering::SeqCst);
        *self.state.lock().unwrap() = ReceiverState::Idle;
        *self.pending_transfer.lock().unwrap() = None;
        *self.pending_stream.lock().unwrap() = None;
    }

    /// Accept the pending transfer and save to the given path
    pub fn accept_transfer(&self, save_path: &PathBuf) -> std::io::Result<()> {
        let transfer = self.pending_transfer.lock().unwrap().take();
        let stream = self.pending_stream.lock().unwrap().take();

        match (transfer, stream) {
            (Some(info), Some(mut conn)) => {
                tracing::info!("✓ User accepted transfer, saving to: {:?}", save_path);
                *self.state.lock().unwrap() = ReceiverState::Receiving;
                self.total_bytes.store(info.file_size, Ordering::SeqCst);
                self.bytes_received.store(0, Ordering::SeqCst);

                // Set data transfer timeouts (longer than handshake)
                if let Err(e) = conn.set_read_timeout(Some(DATA_TIMEOUT)) {
                    tracing::warn!("Failed to set data read timeout: {}", e);
                }
                if let Err(e) = conn.set_write_timeout(Some(DATA_TIMEOUT)) {
                    tracing::warn!("Failed to set data write timeout: {}", e);
                }

                // Send accept response
                tracing::info!("Sending acceptance confirmation to sender...");
                if let Err(e) = conn.write_all(&[1u8]) {
                    // 1 = accepted
                    tracing::error!("Failed to send accept response: {}", e);
                    return Err(e);
                }

                use crate::io_utils::ReceiverWriter;
                use crate::protocol::TransferType;

                // Create or open the writer for resume
                let mut start_chunk_index: u64 = 0;
                let mut received: u64 = 0;
                let mut writer: ReceiverWriter;

                if info.transfer_type == TransferType::Folder {
                    // For folder, we always start fresh for now
                    // TODO: Implement advanced resume for folders
                    tracing::info!("Starting folder transfer (fresh)");
                    writer = ReceiverWriter::new_folder(save_path);
                } else if save_path.exists() {
                     // Check existing file for resume
                    let metadata = std::fs::metadata(save_path)?;
                    let current_len = metadata.len();

                    if current_len > 0 && current_len < info.file_size && info.chunk_size > 0 {
                        // Calculate valid chunks
                        let valid_chunks = current_len / (info.chunk_size as u64);
                        let valid_len = valid_chunks * (info.chunk_size as u64);

                        tracing::info!(
                            "Found existing file ({:?} bytes), resuming from chunk {}",
                            current_len,
                            valid_chunks
                        );

                        // Resume writer
                        writer = ReceiverWriter::resume_single(save_path, valid_len)?;
                        start_chunk_index = valid_chunks;
                        received = valid_len;
                    } else {
                        // Overwrite if full, larger, or invalid chunk size
                        tracing::info!("Overwriting existing file");
                        writer = ReceiverWriter::new_single(save_path)?;
                    }
                } else {
                    writer = ReceiverWriter::new_single(save_path)?;
                }

                // Send resume index
                tracing::info!("Sending resume index {} to sender", start_chunk_index);
                if let Err(e) = conn.write_all(&start_chunk_index.to_be_bytes()) {
                    tracing::error!("Failed to send resume index: {}", e);
                    return Err(e);
                }
                // CRITICAL: flush to ensure sender receives accept + resume index immediately
                if let Err(e) = conn.flush() {
                    tracing::error!("Failed to flush accept response: {}", e);
                    return Err(e);
                }

                tracing::info!("Starting to receive file chunks...");
                let mut last_log_chunk = 0u64;
                loop {
                    // Check if transfer is complete
                    if received >= info.file_size {
                        break;
                    }

                    // Read chunk header: [index: u64][len: u32]
                    let mut header_buf = [0u8; 12];
                    match conn.read_exact(&mut header_buf) {
                        Ok(_) => {}
                        Err(ref e) if e.kind() == std::io::ErrorKind::UnexpectedEof => break,
                        Err(e) => return Err(e),
                    }

                    let chunk_index = u64::from_be_bytes(header_buf[0..8].try_into().unwrap());
                    let chunk_len =
                        u32::from_be_bytes(header_buf[8..12].try_into().unwrap()) as usize;

                    // Log progress periodically (every 100 chunks)
                    if chunk_index - last_log_chunk >= 100 || chunk_index == 0 {
                        tracing::debug!("Receiving chunk {} ({} bytes)", chunk_index, chunk_len);
                        last_log_chunk = chunk_index;
                    }

                    // Read chunk data
                    let mut data = vec![0u8; chunk_len];
                    conn.read_exact(&mut data)?;

                    // Read checksum (16 bytes)
                    let mut chunk_checksum_buf = [0u8; 16];
                    conn.read_exact(&mut chunk_checksum_buf)?;

                    // Verify checksum
                    let calculated_hex = calculate_chunk_checksum(&data);
                    let calculated_bytes: Vec<u8> = (0..std::cmp::min(32, calculated_hex.len()))
                        .step_by(2)
                        .filter_map(|i| u8::from_str_radix(&calculated_hex[i..i + 2], 16).ok())
                        .collect();

                    if calculated_bytes != chunk_checksum_buf {
                        tracing::warn!(
                            "✗ Checksum mismatch for chunk {}, requesting retransmit",
                            chunk_index
                        );
                        // Send ACK with error (1)
                        conn.write_all(&chunk_index.to_be_bytes())?;
                        conn.write_all(&[1u8])?;
                        conn.flush()?; // Flush ACK immediately
                        continue;
                    }

                    // Write to file
                    writer.write_all(&data)?;
                    received += data.len() as u64; // Use actual data len
                    self.bytes_received.store(received, Ordering::SeqCst);

                    // Send ACK success (0)
                    tracing::trace!("✓ Chunk {} verified, sending ACK", chunk_index);
                    conn.write_all(&chunk_index.to_be_bytes())?;
                    conn.write_all(&[0u8])?;
                    conn.flush()?; // Flush ACK immediately to prevent sender timeout
                }

                // Final verification
                tracing::info!("All chunks received, flushing to disk and verifying...");
                writer.flush()?;

                tracing::info!("Calculating final file checksum...");
                let final_checksum = if info.transfer_type == TransferType::Folder {
                     writer.manifest_checksum().ok_or(std::io::Error::new(std::io::ErrorKind::Other, "No manifest checksum"))?
                } else {
                     calculate_file_checksum(save_path)?
                };

                let success = final_checksum == info.file_checksum;

                if success {
                    tracing::info!("✓ Transfer completed successfully! Final checksum verified.");
                    tracing::info!("  Expected: {}", info.file_checksum);
                    tracing::info!("  Received: {}", final_checksum);
                    conn.write_all(&[1u8])?; // Final success
                    let _ = conn.flush(); // Ensure sender receives final result
                    *self.state.lock().unwrap() = ReceiverState::Completed;
                } else {
                    tracing::error!("✗ Final checksum verification failed!");
                    tracing::error!("  Expected: {}", info.file_checksum);
                    tracing::error!("  Received: {}", final_checksum);
                    conn.write_all(&[0u8])?; // Final failure
                    let _ = conn.flush();
                    *self.state.lock().unwrap() = ReceiverState::Error;
                }

                // Important: Reset running flag so we can restart the listener loop
                // The previous listener thread exited when it accepted this connection.
                self.running.store(false, Ordering::SeqCst);

                // Note: We don't auto-restart here because the user might want to see the "Completed" state.
                // The UI should provide a way to "Reset" or "Listen Again".
                // But for "reject", we definitely want to auto-restart.

                Ok(())
            }
            _ => Err(std::io::Error::new(
                std::io::ErrorKind::NotFound,
                "No pending transfer to accept",
            )),
        }
    }

    /// Reject the pending transfer
    pub fn reject_transfer(&self) -> std::io::Result<()> {
        let transfer = self.pending_transfer.lock().unwrap().take();
        let stream = self.pending_stream.lock().unwrap().take();

        if let Some(info) = transfer {
            tracing::info!(
                "✗ User rejected transfer: '{}' from '{}'",
                info.file_name,
                info.sender_name
            );
        }

        if let Some(mut conn) = stream {
            // Send reject response
            tracing::info!("Sending rejection notification to sender...");
            let _ = conn.write_all(&[0u8]); // 0 = rejected
            let _ = conn.flush(); // Ensure sender receives rejection immediately
        }

        *self.state.lock().unwrap() = ReceiverState::Listening;

        // Reset running flag so start() will actually spawn a new thread
        self.running.store(false, Ordering::SeqCst);

        // Restart listening
        tracing::info!("Restarting receiver to listen for next transfer...");
        self.start();

        Ok(())
    }

    /// Get receive progress as percentage
    pub fn progress(&self) -> f32 {
        let total = self.total_bytes.load(Ordering::SeqCst);
        if total == 0 {
            return 0.0;
        }
        let received = self.bytes_received.load(Ordering::SeqCst);
        (received as f32 / total as f32) * 100.0
    }

    /// Get bytes received
    pub fn bytes_received(&self) -> u64 {
        self.bytes_received.load(Ordering::SeqCst)
    }
}

impl Drop for FileReceiverServer {
    fn drop(&mut self) {
        self.stop();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_receiver_creation() {
        let receiver = FileReceiverServer::new().unwrap();
        assert!(receiver.port() > 0);
        assert_eq!(receiver.state(), ReceiverState::Idle);
    }
}
