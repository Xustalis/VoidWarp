//! VoidWarp Transfer Protocol Definitions
//!
//! This module defines the exact byte layout for the handshake and data transfer.

use std::io::{self, Read, Write};

/// P2P Protocol Version (increment when changing handshake format)
pub const PROTOCOL_VERSION: u8 = 1;

/// Handshake Request sent by Sender
/// [VERSION:u8][SENDER_NAME_LEN:u8][SENDER_NAME:bytes][FILE_NAME_LEN:u16][FILE_NAME:bytes]
/// [FILE_SIZE:u64][CHUNK_SIZE:u32][CHECKSUM_LEN:u8][CHECKSUM:bytes]
#[derive(Debug, Clone)]
pub struct HandshakeRequest {
    pub version: u8,
    pub sender_name: String,
    pub file_name: String,
    pub file_size: u64,
    pub chunk_size: u32,
    pub file_checksum: String,
}

impl HandshakeRequest {
    pub fn new(
        sender_name: &str,
        file_name: &str,
        file_size: u64,
        chunk_size: u32,
        file_checksum: &str,
    ) -> Self {
        Self {
            version: PROTOCOL_VERSION,
            sender_name: sender_name.to_string(),
            file_name: file_name.to_string(),
            file_size,
            chunk_size,
            file_checksum: file_checksum.to_string(),
        }
    }

    pub fn write_to<W: Write>(&self, writer: &mut W) -> io::Result<()> {
        let sender_bytes = self.sender_name.as_bytes();
        let file_name_bytes = self.file_name.as_bytes();
        let checksum_bytes = self.file_checksum.as_bytes();

        writer.write_all(&[self.version])?;

        // Sender Name (limit 255 bytes)
        let sender_len = std::cmp::min(sender_bytes.len(), 255) as u8;
        writer.write_all(&[sender_len])?;
        writer.write_all(&sender_bytes[..sender_len as usize])?;

        // File Name (limit 65535 bytes)
        let fname_len = std::cmp::min(file_name_bytes.len(), 65535) as u16;
        writer.write_all(&fname_len.to_be_bytes())?;
        writer.write_all(&file_name_bytes[..fname_len as usize])?;

        // Metadata
        writer.write_all(&self.file_size.to_be_bytes())?;
        writer.write_all(&self.chunk_size.to_be_bytes())?;

        // Checksum (limit 255 bytes usually 32)
        let check_len = std::cmp::min(checksum_bytes.len(), 255) as u8;
        writer.write_all(&[check_len])?;
        writer.write_all(&checksum_bytes[..check_len as usize])?;

        Ok(())
    }

    pub fn read_from<R: Read>(reader: &mut R) -> io::Result<Self> {
        let mut ver_buf = [0u8; 1];
        reader.read_exact(&mut ver_buf)?;
        let version = ver_buf[0];

        if version != PROTOCOL_VERSION {
            return Err(io::Error::new(
                io::ErrorKind::InvalidData,
                format!(
                    "Protocol version mismatch: Expected {}, got {}",
                    PROTOCOL_VERSION, version
                ),
            ));
        }

        // Sender Name
        let mut sender_len_buf = [0u8; 1];
        reader.read_exact(&mut sender_len_buf)?;
        let sender_len = sender_len_buf[0] as usize;
        let mut sender_buf = vec![0u8; sender_len];
        reader.read_exact(&mut sender_buf)?;
        let sender_name = String::from_utf8_lossy(&sender_buf).to_string();

        // File Name
        let mut fname_len_buf = [0u8; 2];
        reader.read_exact(&mut fname_len_buf)?;
        let fname_len = u16::from_be_bytes(fname_len_buf) as usize;
        let mut fname_buf = vec![0u8; fname_len];
        reader.read_exact(&mut fname_buf)?;
        let file_name = String::from_utf8_lossy(&fname_buf).to_string();

        // Metadata
        let mut size_buf = [0u8; 8];
        reader.read_exact(&mut size_buf)?;
        let file_size = u64::from_be_bytes(size_buf);

        let mut chunk_buf = [0u8; 4];
        reader.read_exact(&mut chunk_buf)?;
        let chunk_size = u32::from_be_bytes(chunk_buf);

        // Checksum
        let mut check_len_buf = [0u8; 1];
        reader.read_exact(&mut check_len_buf)?;
        let check_len = check_len_buf[0] as usize;
        let mut check_buf = vec![0u8; check_len];
        reader.read_exact(&mut check_buf)?;
        let file_checksum = String::from_utf8_lossy(&check_buf).to_string();

        Ok(Self {
            version,
            sender_name,
            file_name,
            file_size,
            chunk_size,
            file_checksum,
        })
    }
}
