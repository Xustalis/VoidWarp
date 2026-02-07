//! Checksum Module
//!
//! Provides MD5 checksum calculation for file integrity verification.

use std::fs::File;
use std::io::{BufReader, Read};
use std::path::Path;

/// Default buffer size for file reading (4MB)
const BUFFER_SIZE: usize = 4 * 1024 * 1024;

/// Calculate MD5 checksum of a file
pub fn calculate_file_checksum(path: &Path) -> std::io::Result<String> {
    let file = File::open(path)?;
    let mut reader = BufReader::with_capacity(BUFFER_SIZE, file);
    let mut context = md5::Context::new();
    let mut buffer = vec![0u8; BUFFER_SIZE];

    loop {
        let bytes_read = reader.read(&mut buffer)?;
        if bytes_read == 0 {
            break;
        }
        context.consume(&buffer[..bytes_read]);
    }

    let digest = context.compute();
    Ok(format!("{:x}", digest))
}

/// Calculate MD5 checksum of a byte slice (for chunks)
pub fn calculate_chunk_checksum(data: &[u8]) -> String {
    let digest = md5::compute(data);
    format!("{:x}", digest)
}

/// Calculate MD5 checksum of a byte slice and return raw bytes
pub fn calculate_chunk_checksum_raw(data: &[u8]) -> [u8; 16] {
    let digest = md5::compute(data);
    digest.into()
}

/// Verify file checksum matches expected value
pub fn verify_file_checksum(path: &Path, expected: &str) -> std::io::Result<bool> {
    let actual = calculate_file_checksum(path)?;
    Ok(actual.eq_ignore_ascii_case(expected))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;
    use tempfile::NamedTempFile;

    #[test]
    fn test_chunk_checksum() {
        let data = b"Hello, VoidWarp!";
        let checksum = calculate_chunk_checksum(data);
        assert!(!checksum.is_empty());
        assert_eq!(checksum.len(), 32); // MD5 hex is 32 chars
    }

    #[test]
    fn test_file_checksum() {
        let mut temp = NamedTempFile::new().unwrap();
        temp.write_all(b"Test file content").unwrap();
        temp.flush().unwrap();

        let checksum = calculate_file_checksum(temp.path()).unwrap();
        assert_eq!(checksum.len(), 32);

        // Verify same content produces same hash
        let checksum2 = calculate_file_checksum(temp.path()).unwrap();
        assert_eq!(checksum, checksum2);
    }

    #[test]
    fn test_verify_checksum() {
        let mut temp = NamedTempFile::new().unwrap();
        temp.write_all(b"Verify me").unwrap();
        temp.flush().unwrap();

        let checksum = calculate_file_checksum(temp.path()).unwrap();
        assert!(verify_file_checksum(temp.path(), &checksum).unwrap());
        assert!(!verify_file_checksum(temp.path(), "wrong_hash").unwrap());
    }
}
