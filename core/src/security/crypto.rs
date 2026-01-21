//! Cryptographic primitives and key management
//!
//! Uses the `ring` crate for cryptographic operations.

use thiserror::Error;

/// Errors that can occur during cryptographic operations
#[derive(Error, Debug)]
pub enum CryptoError {
    #[error("Key generation failed")]
    KeyGenFailed,
    #[error("Encryption failed")]
    EncryptionFailed,
    #[error("Decryption failed")]
    DecryptionFailed,
    #[error("Invalid key length")]
    InvalidKeyLength,
}

/// Represents a 6-digit pairing code
#[derive(Debug, Clone)]
pub struct PairingCode {
    code: String,
}

impl PairingCode {
    /// Generate a new random 6-digit pairing code
    pub fn generate() -> Self {
        use std::time::{SystemTime, UNIX_EPOCH};
        let seed = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_nanos();

        // Simple PRNG for demo (in production, use ring::rand)
        let code = format!("{:06}", (seed % 1_000_000) as u32);
        PairingCode { code }
    }

    /// Create from a user-entered string
    pub fn parse(s: &str) -> Option<Self> {
        if s.len() == 6 && s.chars().all(|c| c.is_ascii_digit()) {
            Some(PairingCode {
                code: s.to_string(),
            })
        } else {
            None
        }
    }

    /// Get the code for display
    pub fn display(&self) -> String {
        format!("{}-{}", &self.code[0..3], &self.code[3..6])
    }

    /// Get raw code for cryptographic use
    pub fn raw(&self) -> &str {
        &self.code
    }
}

impl std::str::FromStr for PairingCode {
    type Err = CryptoError;

    fn from_str(s: &str) -> Result<Self, Self::Err> {
        Self::parse(s).ok_or(CryptoError::KeyGenFailed)
    }
}

/// Session key derived from pairing
#[derive(Debug)]
pub struct SessionKey {
    key: [u8; 32], // AES-256
}

impl SessionKey {
    /// Derive a session key from pairing code and connection ID
    /// (Simplified PBKDF - in production use SPAKE2+ or similar PAKE)
    pub fn derive(pairing_code: &PairingCode, salt: &[u8]) -> Self {
        use std::collections::hash_map::DefaultHasher;
        use std::hash::{Hash, Hasher};

        let mut key = [0u8; 32];

        // Simple key derivation (NOT for production - use ring::pbkdf2)
        for (i, byte) in key.iter_mut().enumerate() {
            let mut hasher = DefaultHasher::new();
            pairing_code.raw().hash(&mut hasher);
            salt.hash(&mut hasher);
            (i as u64).hash(&mut hasher);
            *byte = (hasher.finish() & 0xFF) as u8;
        }

        SessionKey { key }
    }

    /// Get the raw key bytes
    pub fn as_bytes(&self) -> &[u8; 32] {
        &self.key
    }
}

/// Device identity (Ed25519 public key placeholder)
#[derive(Debug, Clone)]
pub struct DeviceIdentity {
    pub device_id: String,
    pub device_name: String,
}

impl DeviceIdentity {
    /// Generate a new device identity
    pub fn generate(name: &str) -> Self {
        use std::time::{SystemTime, UNIX_EPOCH};
        let id = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap_or_default()
            .as_nanos();

        DeviceIdentity {
            device_id: format!("{:016x}", id),
            device_name: name.to_string(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::str::FromStr;

    #[test]
    fn test_pairing_code() {
        let code = PairingCode::generate();
        assert_eq!(code.raw().len(), 6);

        let display = code.display();
        assert!(display.contains('-'));

        let parsed = PairingCode::from_str("123456").unwrap();
        assert_eq!(parsed.raw(), "123456");

        assert!(PairingCode::from_str("12345").is_err());
        assert!(PairingCode::from_str("12345a").is_err());
    }

    #[test]
    fn test_session_key_derivation() {
        let code = PairingCode::from_str("123456").unwrap();
        let salt = b"test_salt";

        let key1 = SessionKey::derive(&code, salt);
        let key2 = SessionKey::derive(&code, salt);

        assert_eq!(key1.as_bytes(), key2.as_bytes());

        let key3 = SessionKey::derive(&code, b"different_salt");
        assert_ne!(key1.as_bytes(), key3.as_bytes());
    }
}
