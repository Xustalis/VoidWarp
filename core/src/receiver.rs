//! File Receiver Module
//!
//! TCP-based file receiver for accepting incoming file transfers.

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream, SocketAddr};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;
use std::fs::File;

/// Incoming transfer request information
#[derive(Debug, Clone)]
pub struct IncomingTransfer {
    pub sender_name: String,
    pub sender_addr: SocketAddr,
    pub file_name: String,
    pub file_size: u64,
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
    pub fn new() -> std::io::Result<Self> {
        // Bind to any available port
        let listener = TcpListener::bind("0.0.0.0:0")?;
        let port = listener.local_addr()?.port();
        
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
                        
                        tracing::info!(
                            "Incoming transfer: '{}' ({} bytes) from '{}'",
                            file_name, file_size, sender_name
                        );
                        
                        // Store pending transfer info
                        let transfer = IncomingTransfer {
                            sender_name,
                            sender_addr: addr,
                            file_name,
                            file_size,
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
                
                // Send accept response
                conn.write_all(&[1u8])?; // 1 = accepted
                
                // Create the file
                let mut file = File::create(save_path)?;
                
                // Receive the file data
                let mut buffer = vec![0u8; 64 * 1024]; // 64KB buffer
                let mut received: u64 = 0;
                
                while received < info.file_size {
                    let to_read = std::cmp::min(buffer.len() as u64, info.file_size - received) as usize;
                    let n = conn.read(&mut buffer[..to_read])?;
                    if n == 0 {
                        return Err(std::io::Error::new(
                            std::io::ErrorKind::UnexpectedEof,
                            "Connection closed before transfer complete",
                        ));
                    }
                    file.write_all(&buffer[..n])?;
                    received += n as u64;
                    self.bytes_received.store(received, Ordering::SeqCst);
                }
                
                file.flush()?;
                *self.state.lock().unwrap() = ReceiverState::Completed;
                tracing::info!("Transfer completed: {} bytes received", received);
                
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
