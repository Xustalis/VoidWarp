# Future Features & Technical Reserve

> This document tracks features that were deferred from the MVP to reduce complexity, but are critical for the long-term vision.

## 1. Advanced Networking
- **WAN Support (v2.0)**:
  - Implement Global Relay Servers (TURN).
  - ICE (Interactive Connectivity Establishment) for complex NATs.
- **IPv6-Only Networks**: Full testing and support for IPv6-only environments (MVP supports Dual Stack).

## 2. Advanced Security
- **Secure Deletion (DoD 5220.22-M)**:
  - **Spec**: overwrite with zeros, then ones, then random data.
  - **Reason for Deferral**: Requires OS-level low-level file system control which varies significantly by OS and FS (APFS vs NTFS vs ext4).
- **YubiKey / Hardware Token Support**: For enterprise environments.

## 3. Performance Optimization
- **Parallel Streams**:
  - Sending multiple files concurrently on different Stream IDs.
  - **Current**: Sequential queue.
- **Compression**: 
  - Zstd compression for text/compressible types before encryption.

## 4. User Experience
- **Clipboard Sharing**: Sync clipboard text/images across LAN.
- **Remote Control**: Simple input forwarding (KVM style).
