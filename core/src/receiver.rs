//! File Receiver Module
//!
//! TCP-based file receiver for accepting incoming file transfers.

use std::fs::File;
use std::io::{Read, Write};
use std::net::{SocketAddr, TcpListener, TcpStream};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;

use crate::checksum::{calculate_chunk_checksum, calculate_file_checksum};
use std::time::Duration;

// Timeouts
const HANDSHAKE_TIMEOUT: Duration = Duration::from_secs(10);
const DATA_TIMEOUT: Duration = Duration::from_secs(30);

/// Incoming transfer request information
#[derive(Debug, Clone)]
pub struct IncomingTransfer {
    pub sender_name: String,
    pub sender_addr: SocketAddr,
    pub file_name: String,
    pub file_size: u64,
    pub file_checksum: String, // Hex string
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
        let (listener, port) = match TcpListener::bind("0.0.0.0:42424") {
            Ok(l) => {
                tracing::info!("FileReceiverServer bound to standard port 42424");
                (l, 42424)
            }
            Err(_) => {
                // Fall back to any available port
                let l = TcpListener::bind("0.0.0.0:0")?;
                let p = l.local_addr()?.port();
                tracing::info!("Port 42424 in use, bound to fallback port {}", p);
                (l, p)
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
            tracing::info!("Receiver thread started");

            while running.load(Ordering::SeqCst) {
                match listener.accept() {
                    Ok((mut stream, addr)) => {
                        tracing::info!("Incoming connection from {}", addr);

                        // Set handshake timeouts
                        if let Err(e) = stream.set_read_timeout(Some(HANDSHAKE_TIMEOUT)) {
                            tracing::warn!("Failed to set read timeout: {}", e);
                        }
                        if let Err(e) = stream.set_write_timeout(Some(HANDSHAKE_TIMEOUT)) {
                            tracing::warn!("Failed to set write timeout: {}", e);
                        }

                        // Read the file transfer handshake
                        // Protocol: [sender_name_len:u8][sender_name][file_name_len:u16][file_name][file_size:u64]
                        let mut header = [0u8; 1];
                        if stream.read_exact(&mut header).is_err() {
                            tracing::warn!("Failed to read sender name length");
                            continue;
                        }

                        let sender_name_len = header[0] as usize;
                        let mut sender_name_buf = vec![0u8; sender_name_len];
                        if stream.read_exact(&mut sender_name_buf).is_err() {
                            tracing::warn!("Failed to read sender name");
                            continue;
                        }
                        let sender_name = String::from_utf8_lossy(&sender_name_buf).to_string();

                        let mut file_name_len_buf = [0u8; 2];
                        if stream.read_exact(&mut file_name_len_buf).is_err() {
                            tracing::warn!("Failed to read file name length");
                            continue;
                        }
                        let file_name_len = u16::from_be_bytes(file_name_len_buf) as usize;

                        let mut file_name_buf = vec![0u8; file_name_len];
                        if stream.read_exact(&mut file_name_buf).is_err() {
                            tracing::warn!("Failed to read file name");
                            continue;
                        }
                        let file_name = String::from_utf8_lossy(&file_name_buf).to_string();

                        let mut file_size_buf = [0u8; 8];
                        if stream.read_exact(&mut file_size_buf).is_err() {
                            tracing::warn!("Failed to read file size");
                            continue;
                        }
                        let file_size = u64::from_be_bytes(file_size_buf);

                        // Read checksum (hex string)
                        let mut checksum_len_buf = [0u8; 1];
                        if stream.read_exact(&mut checksum_len_buf).is_err() {
                            tracing::warn!("Failed to read checksum length");
                            continue;
                        }
                        let checksum_len = checksum_len_buf[0] as usize;
                        let mut checksum_buf = vec![0u8; checksum_len];
                        if stream.read_exact(&mut checksum_buf).is_err() {
                            tracing::warn!("Failed to read checksum");
                            continue;
                        }
                        let file_checksum = String::from_utf8_lossy(&checksum_buf).to_string();

                        tracing::info!(
                            "Incoming transfer: '{}' ({} bytes) from '{}'",
                            file_name,
                            file_size,
                            sender_name
                        );

                        // Store pending transfer info
                        let transfer = IncomingTransfer {
                            sender_name,
                            sender_addr: addr,
                            file_name,
                            file_size,
                            file_checksum,
                        };

                        *pending_transfer.lock().unwrap() = Some(transfer);
                        *pending_stream.lock().unwrap() = Some(stream);
                        *state.lock().unwrap() = ReceiverState::AwaitingAccept;

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
                if let Err(e) = conn.write_all(&[1u8]) {
                    // 1 = accepted
                    tracing::error!("Failed to send accept: {}", e);
                    return Err(e);
                }

                // Create the file
                let mut file = File::create(save_path)?;

                // Receive the file in chunks
                let mut received: u64 = 0;

                // Send resume index (0)
                let _ = conn.write_all(&0u64.to_be_bytes());

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

                    // Read chunk data
                    let mut data = vec![0u8; chunk_len];
                    conn.read_exact(&mut data)?;

                    // Read checksum (8 bytes)
                    let mut chunk_checksum_buf = [0u8; 8];
                    conn.read_exact(&mut chunk_checksum_buf)?;

                    // Verify checksum
                    let calculated_hex = calculate_chunk_checksum(&data);
                    let calculated_bytes: Vec<u8> = (0..std::cmp::min(16, calculated_hex.len()))
                        .step_by(2)
                        .filter_map(|i| u8::from_str_radix(&calculated_hex[i..i + 2], 16).ok())
                        .collect();

                    if calculated_bytes != chunk_checksum_buf {
                        tracing::warn!("Checksum mismatch for chunk {}", chunk_index);
                        // Send ACK with error (1)
                        conn.write_all(&chunk_index.to_be_bytes())?;
                        conn.write_all(&[1u8])?;
                        continue;
                    }

                    // Write to file
                    file.write_all(&data)?;
                    received += data.len() as u64; // Use actual data len
                    self.bytes_received.store(received, Ordering::SeqCst);

                    // Send ACK success (0)
                    conn.write_all(&chunk_index.to_be_bytes())?;
                    conn.write_all(&[0u8])?;
                }

                // Final verification
                file.flush()?;

                let final_checksum = calculate_file_checksum(save_path)?;
                let success = final_checksum == info.file_checksum;

                if success {
                    tracing::info!("Transfer completed and verified!");
                    conn.write_all(&[1u8])?; // Final success
                    *self.state.lock().unwrap() = ReceiverState::Completed;
                } else {
                    tracing::error!("Final checksum verification failed");
                    conn.write_all(&[0u8])?; // Final failure
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
        let _transfer = self.pending_transfer.lock().unwrap().take();
        let stream = self.pending_stream.lock().unwrap().take();

        if let Some(mut conn) = stream {
            // Send reject response
            let _ = conn.write_all(&[0u8]); // 0 = rejected
        }

        *self.state.lock().unwrap() = ReceiverState::Listening;

        // Reset running flag so start() will actually spawn a new thread
        self.running.store(false, Ordering::SeqCst);

        // Restart listening
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
