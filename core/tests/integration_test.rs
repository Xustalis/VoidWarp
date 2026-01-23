use std::io::Write;
use std::net::SocketAddr;
use std::thread;
use std::time::Duration;
use tempfile::NamedTempFile;
use voidwarp_core::receiver::{FileReceiverServer, ReceiverState};
use voidwarp_core::sender::{TcpFileSender, TransferResult};
use voidwarp_core::checksum::calculate_file_checksum;

#[test]
fn test_sender_receiver_integration() {
    // 1. Setup Receiver
    let receiver = FileReceiverServer::new().expect("Failed to create receiver");
    let port = receiver.port();
    receiver.start();
    
    // 2. Setup Sender
    let mut temp_src = NamedTempFile::new().unwrap();
    let content = b"Integration test content for VoidWarp transfer protocol mismatch fix.";
    temp_src.write_all(content).unwrap();
    temp_src.flush().unwrap();
    
    let src_path = temp_src.path().to_str().unwrap();
    let sender = TcpFileSender::new(src_path).expect("Failed to create sender");
    
    // 3. Start Transfer in separate thread (Sender blocks)
    let sender_handle = thread::spawn(move || {
        let addr = format!("127.0.0.1:{}", port).parse::<SocketAddr>().unwrap();
        sender.send_to(addr, "TestSender")
    });
    
    // 4. Receiver Logic
    // Wait for connection
    let mut tries = 0;
    while receiver.state() != ReceiverState::AwaitingAccept {
        thread::sleep(Duration::from_millis(100));
        tries += 1;
        if tries > 20 { // 2 seconds timeout
            panic!("Timeout waiting for connection. State: {:?}", receiver.state());
        }
    }
    
    // Accept transfer
    let output_dir = tempfile::tempdir().unwrap();
    let save_path = output_dir.path().join("received_file.txt");
    
    // Verify pending info
    let pending = receiver.pending_transfer().expect("No pending transfer");
    assert_eq!(pending.file_size, content.len() as u64);
    // Checksum verification is part of protocol now, receiver has it in pending (if we added it to struct, which we did)
    // assert!(!pending.file_checksum.is_empty()); // Field was added in our refactor
    
    receiver.accept_transfer(&save_path).expect("Failed to accept transfer");
    
    // Wait for completion
    tries = 0;
    while receiver.state() != ReceiverState::Completed {
        thread::sleep(Duration::from_millis(100));
        tries += 1;
        if tries > 50 { // 5 seconds timeout
            panic!("Timeout waiting for completion. State: {:?}", receiver.state());
        }
    }
    
    // 5. Verify Result
    let result = sender_handle.join().unwrap();
    match result {
        TransferResult::Success => println!("Sender reported success"),
        err => panic!("Sender reported error: {:?}", err),
    }
    
    // Check file content
    let received_content = std::fs::read(&save_path).unwrap();
    assert_eq!(received_content, content);
    
    // Checksum double check
    let src_sum = calculate_file_checksum(temp_src.path()).unwrap();
    let dst_sum = calculate_file_checksum(&save_path).unwrap();
    assert_eq!(src_sum, dst_sum);
}
