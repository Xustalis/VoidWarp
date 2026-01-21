use bytes::{Buf, BufMut, Bytes, BytesMut};
use thiserror::Error;

/// Packet Type Definitions (4 bits)
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum PacketType {
    Initial = 0x00,
    Handshake = 0x01,
    Data = 0x02,
    Ack = 0x03,
    KeepAlive = 0x04,
    Close = 0x05,
    Unknown = 0xFF,
}

impl From<u8> for PacketType {
    fn from(byte: u8) -> Self {
        match byte & 0x0F {
            0x00 => PacketType::Initial,
            0x01 => PacketType::Handshake,
            0x02 => PacketType::Data,
            0x03 => PacketType::Ack,
            0x04 => PacketType::KeepAlive,
            0x05 => PacketType::Close,
            _ => PacketType::Unknown,
        }
    }
}

/// VWTP Packet Header
/// Flags (1) | Connection ID (8) | Packet Number (8)
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Header {
    pub packet_type: PacketType,
    pub key_phase: bool,
    pub connection_id: u64,
    pub packet_number: u64,
}

impl Header {
    pub const SIZE: usize = 1 + 8 + 8;

    pub fn encode(&self, buf: &mut BytesMut) {
        let mut flags = self.packet_type as u8;
        if self.key_phase {
            flags |= 0x10;
        }
        buf.put_u8(flags);
        buf.put_u64_le(self.connection_id);
        buf.put_u64_le(self.packet_number);
    }

    pub fn decode(buf: &mut Bytes) -> Result<Self, PacketError> {
        if buf.remaining() < Self::SIZE {
            return Err(PacketError::Incomplete);
        }

        let flags = buf.get_u8();
        let packet_type = PacketType::from(flags);
        if packet_type == PacketType::Unknown {
            return Err(PacketError::InvalidType(flags));
        }

        let key_phase = (flags & 0x10) != 0;
        let connection_id = buf.get_u64_le();
        let packet_number = buf.get_u64_le();

        Ok(Header {
            packet_type,
            key_phase,
            connection_id,
            packet_number,
        })
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Packet {
    pub header: Header,
    pub payload: Bytes,
}

impl Packet {
    pub fn encode(&self, buf: &mut BytesMut) {
        self.header.encode(buf);
        buf.put(self.payload.clone());
    }

    pub fn decode(mut buf: Bytes) -> Result<Self, PacketError> {
        let header = Header::decode(&mut buf)?;
        // Remaining bytes are payload
        let payload = buf;
        Ok(Packet { header, payload })
    }
}

#[derive(Error, Debug)]
pub enum PacketError {
    #[error("Packet data incomplete")]
    Incomplete,
    #[error("Invalid packet type: {0:#x}")]
    InvalidType(u8),
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encode_decode_data() {
        let header = Header {
            packet_type: PacketType::Data,
            key_phase: true,
            connection_id: 0x1234567890ABCDEF,
            packet_number: 1,
        };
        let payload = Bytes::from_static(b"Hello VoidWarp");
        let packet = Packet {
            header: header.clone(),
            payload: payload.clone(),
        };

        let mut buf = BytesMut::new();
        packet.encode(&mut buf);

        assert_eq!(buf.len(), Header::SIZE + payload.len());

        let decoded = Packet::decode(buf.freeze()).expect("Decode failed");
        assert_eq!(decoded, packet);
        assert_eq!(decoded.header.packet_type, PacketType::Data);
        assert!(decoded.header.key_phase);
    }
}
