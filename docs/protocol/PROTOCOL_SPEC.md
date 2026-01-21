# VoidWarp Transport Protocol (VWTP) Specification

## 1. Overview
VWTP is a user-space reliable UDP transport protocol designed for maximum throughput on LAN environments. It borrows concepts from QUIC but is simplified for peer-to-peer file transfer without the need for global internet routing optimization (though it supports NAT traversal).

## 2. Packet Structure
All packets are sent over UDP.
Little Endian is used for all fields.

### 2.1 Common Header
| Offset | Size (Bytes) | Field | Description |
|---|---|---|---|
| 0 | 1 | Flags | Bitmask (Type, KeyPhase, Reserved) |
| 1 | 8 | Connection ID | 64-bit Random ID for session |
| 9 | 8 | Packet Number | Monotonically increasing counter |

### 2.2 Packet Types (Flag Bits 0-3)
- `0x00`: **Initial** (Client Hello)
- `0x01`: **Handshake** (Server Hello / Key Exchange)
- `0x02`: **Data** (Encrypted Payload)
- `0x03`: **Ack** (Selective Acknowledgement)
- `0x04`: **KeepAlive** (Heartbeat)
- `0x05`: **Close** (Connection Termination)

## 3. Reliability Mechanism
- **Selective ACK (SACK)**: Receivers ACK ranges of received packet numbers (e.g., "Received 100-150, 155-160").
- **Retransmission**: Senders maintain a buffer of unacknowledged packets. If a packet is missing from ACKs for > 1.5x RTT, it is retransmitted with a *new* packet number but same stream offset.

## 4. Flow Control
- **Stream Flow Control**: Each stream has a `MaxStreamData` offset.
- **Connection Flow Control**: The total connection has a `MaxData` offset.
- **Updates**: `MAX_DATA` frames sent by receiver to grant more credit.

## 5. Streams
VWTP supports multiplexed streams within a single UDP connection:
- **Stream ID 0**: **Control Stream** (Protobuf Messages: Offer, Accept, etc.) - High Priority.
- **Stream ID 1+**: **Data Streams** (File Content) - Normal Priority.
