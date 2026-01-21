# VoidWarp Security Specification

## 1. Security Model
- **Trust Model**: TOFU (Trust On First Use) or Explicit Pairing.
- **Encryption**: End-to-End Encryption (E2EE) required for all transfers.
- **Forward Secrecy**: Required. Ephemeral keys generated per session.

## 2. Cryptographic Primitives
- **Key Exchange**: X25519 (Elliptic Curve Diffie-Hellman)
- **AEAD (Symmetric)**: 
  - `ChaCha20-Poly1305` (Mobile/ARM preference)
  - `AES-256-GCM` (Desktop/x64 preference with hardware acceleration)
- **Hashing**: SHA-256

## 3. Pairing Mechanism (Auth)
To authenticate two devices on the LAN without a central server:

### 3.1 SPAKE2+ Handshake
We use **SPAKE2+** (Simple Password Authenticated Key Exchange) with a 6-digit short PIN.
1. **User A** generates a 6-digit code (e.g., `123-456`).
2. **User B** enters the code.
3. Both devices derive a strong session key from the weak PIN + internal salts.
4. If successful, they exchange long-term **Device Identity Keys (Ed25519)**.
5. Future connections use the Identity Keys for mutual authentication (mTLS style) without PIN.

## 4. Transport Security (TLS 1.3 over UDP)
We integrate `rustls` to handle the TLS 1.3 state machine.
- **Record Layer**: VWTP acts as the record layer.
- **Handshake**: TLS ClientHello / ServerHello carried in VWTP `Handshake` packets.
- **Rekeying**: Keys rotated every 1GB of data or 1 hour.

## 5. Storage Security
- **Temporary Files**: Encrypted at rest using a per-transfer random key.
- **Cleanup**: `File::set_len(0)` followed by unlink to mitigate forensic recovery (DoD 5220.22-M is planned for v2).
