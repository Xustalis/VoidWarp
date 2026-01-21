# VoidWarp Project Roadmap

## Phase 1: Core Library (The Engine) - [Month 1]
**Goal**: A robust, headless, cross-platform Rust library (`libvoidwarp`) that can discovery peers and transfer files reliably.
- [ ] **Transport Layer**: Implement `VWTP` (Protocol Spec).
- [ ] **Discovery**: Implement mDNS responder and browser.
- [ ] **Security**: Implement SPAKE2+ handshake and TLS 1.3 tunnel.
- [ ] **API**: Expose C-ABI and generate UniFFI bindings.

## Phase 2: Windows MVP - [Month 1.5]
**Goal**: A functional Windows Client.
- [ ] **UI**: WPF Application with MVVM pattern.
- [ ] **Integration**: Bind `libvoidwarp.dll`.
- [ ] **Feature**: Send/Receive single file on LAN.

## Phase 3: Apple Ecosystem - [Month 2.5]
**Goal**: macOS and iOS Clients.
- [ ] **UI**: Shared SwiftUI codebase for Mac/iOS.
- [ ] **Integration**: `libvoidwarp.a` static lib linkage.
- [ ] **Feature**: AirDrop-like experience.

## Phase 4: Android Ecosystem - [Month 3.5]
**Goal**: Android Client.
- [ ] **UI**: Jetpack Compose.
- [ ] **Integration**: JNI Bridge to `libvoidwarp.so`.

## Phase 5: Polish & Security V1 - [Month 4]
**Goal**: Production readiness.
- [ ] **Recovery**: Resume interrupted transfers (Checkpointing).
- [ ] **Perf**: ASM optimizations for Crypto (AES-NI / NEON).
- [ ] **Audit**: Third-party logic review.

## Phase 6: Release - [Month 4.5]
- [ ] **Distribution**: MSI, DMG, Play Store, TestFlight.
- [ ] **Docs**: User Manuals.
