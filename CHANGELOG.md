# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
