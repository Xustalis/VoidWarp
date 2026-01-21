# VoidWarp Message Format

## 1. Control Plane Messages
Control messages effectively implement an RPC system over **Stream 0**.
Format: **Protobuf 3**.

```protobuf
syntax = "proto3";
package voidwarp.control;

message Envelope {
  string request_id = 1;
  oneof payload {
    Hello hello = 2;
    Offer offer = 3;
    Answer answer = 4;
    TransferUpdate progress = 5;
    Error error = 6;
  }
}

message Hello {
  string device_name = 1;
  string device_id = 2; // Hex encoded Ed25519 Public Key
  Capabilities caps = 3;
}

message Offer {
  string session_id = 1;
  uint64 total_size = 2;
  uint32 total_files = 3;
  repeated FileInfo files = 4;
}

message FileInfo {
  string path = 1; // Relative path
  uint64 size = 2;
  string mime_type = 3;
  uint64 last_modified = 4;
  string sha256_hash = 5; // Optional (computed mostly on fly)
}

message Answer {
  string session_id = 1;
  bool accepted = 2;
  repeated string skipped_files = 3; // Files user chose not to download
}
```

## 2. Data Plane Messages
Data messages flow over **Stream 1+**.
Format: **Raw Binary Chunk**.

### 2.1 Chunk Structure
Unlike TCP streams, VWTP preserves chunk boundaries if needed, but usually we treat file transfer as a byte stream.
- **Header**: Stream Frame Header (defined in PROTOCOL_SPEC).
- **Body**: Raw bytes of the file.

### 2.2 Multi-File Ordering
Files are sent sequentially on the stream to avoid fragmentation overhead, OR multiplexed on different stream IDs if parallel transfer is enabled (Roadmap Feature).
- **MVP**: Sequential. File 1 finishes -> File 2 starts.
