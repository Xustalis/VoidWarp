# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] - 2026-02-08

### Performance 🚀
- **Streaming Mode**: Files under 32MB now transfer as a single chunk, eliminating round-trip delays
- **TCP_NODELAY**: Disabled Nagle's algorithm to remove ACK latency (30x speed improvement for small files)
- **Optimized Buffers**: Increased chunk sizes to 1-2MB and I/O buffers to 1MB for maximum throughput
- **Incremental Hashing**: File checksums calculated on-the-fly during transfer, saving a full read pass

### Stability & Security 🛡️
- **Protocol Hardening**: Added safety caps for chunk_size (32MB) and filename length (1024 chars) to prevent OOM attacks
- **UI Resilience**: Android transfers now use try-finally blocks to guarantee state cleanup on errors
- **Allocation Guards**: Receiver validates chunk sizes before buffer allocation

### Fixes 🔧
- Fixed formatting and clippy warnings to pass CI checks
- Resolved variable shadowing and unused import warnings
- Improved error handling across all platforms

## [1.0.0] - 2026-02-07

### Added
- Initial release of VoidWarp
- Cross-platform file transfer between Android and Windows
- End-to-end encryption using X25519 + AES-256-GCM
- mDNS auto-discovery with manual peer addition fallback
- Multi-file sequential transfer support
- Folder transfer with structure preservation
- Transfer history management
- Dark cyberpunk theme across all platforms
- Self-contained Windows installer (no .NET required)
- Project website with GitHub Pages deployment

### Technical
- Rust core library with FFI/JNI bindings
- Custom VWTP (VoidWarp Transport Protocol) over UDP
- Windows WPF client with native integration
- Android Jetpack Compose client
- GitHub Actions CI/CD pipeline
