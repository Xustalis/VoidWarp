# VoidWarp Technical Constitution (SKILL.md)

> This document defines the non-negotiable technical standards, engineering practices, and architectural decisions for the VoidWarp project. It serves as the single source of truth for engineering quality.

## 1. Core Technology Decisions

### 1.1 Core Library (The Engine)
**Language**: **Rust** (Edition 2021+)
**Rationale**: 
- **Memory Safety**: Critical for implementing a custom UDP transport protocol and p2p networking without buffer overflows or data races.
- **Cross-Platform Tooling**: Superior dependency management (Cargo) and cross-compilation support compared to C++.
- **Interoperability**: First-class support for generating bindings for Swift (iOS/macOS) and Kotlin (Android) via tools like `Mozilla UniFFI`.

### 1.2 Platform Implementations (The UI/OS Layer)
- **Windows**: C# / WPF (.NET 8.0)
  - Layout: XAML
  - Interop: P/Invoke to Rust Core DLL
- **macOS/iOS**: Swift 5.9+ / SwiftUI
  - Interop: UniFFI generated bindings
- **Android**: Kotlin / Jetpack Compose
  - Interop: UniFFI generated bindings (JNI)

## 2. Engineering Standards

### 2.1 Code Quality & Formatting
- **Rust**:
  - Must pass `cargo clippy -- -D warnings` (Strict Mode) and `cargo fmt`.
  - No `unwrap()` in production code (use `expect` with context or robust error handling).
  - Documentation: All public structs and functions must have rustdoc comments (`///`).
- **C#**: Follow Microsoft C# Coding Conventions.
- **Swift**: Follow Swift API Design Guidelines.
- **Kotlin**: Follow Android Kotlin Style Guide.

### 2.2 Version Control (Git)
**Commit Message Format**: [Conventional Commits](https://www.conventionalcommits.org/)
```text
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```
- **Types**:
  - `feat`: New feature
  - `fix`: Bug fix
  - `docs`: Documentation only
  - `style`: Formatting, missing semi-colons, etc.
  - `refactor`: Code change that neither fixes a bug nor adds a feature
  - `perf`: Code change that improves performance
  - `test`: Adding missing tests
  - `chore`: Build process, dependency updates

**Branching Strategy**:
- `main`: Stable, releasable code.
- `dev`: Integration branch.
- `feat/feature-name`: Feature branches.

### 2.3 Directory Structure Standard
```text
VoidWarp/
├── .github/            # CI/CD workflows
├── docs/               # Architecture, Protocol, and Design Docs
├── core/               # Rust Core Library (The "Engine")
│   ├── src/
│   ├── tests/
│   └── Cargo.toml
├── platforms/          # Platform Specific Implementations
│   ├── windows/        # WPF Solution
│   ├── android/        # Android Studio Project
│   ├── apple/          # Xcode Workspace (Shared iOS/macOS)
└── tools/              # Build scripts, CI helpers
```

## 3. Critical Technical Constraints
1. **Zero-Copy**: Where possible, data transfer between Core and UI should minimize copying.
2. **Async/Await**: All network I/O must be asynchronous.
   - Rust: `tokio` runtime.
   - C#: `async`/`await`.
   - Swift: `async`/`await`.
   - Kotlin: Coroutines.
3. **Safety**:
   - TLS 1.3 mandated for all control and data channels.
   - No hardcoded secrets in the codebase.

## 4. Documentation Requirements
- **Architecture**: C4 Model diagrams required for all major components.
- **API**: OpenAPI 3.0 spec for any HTTP interfaces (if applicable), standard Interface Definition Language (UDL) for Core->UI bridge.
- **Protocol**: Custom protocol must be fully specified in `docs/protocol/` before implementation.
