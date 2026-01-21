# CI/CD Design

## 1. Workflow
We will use **GitHub Actions** for all automation.

## 2. Matrix Strategy
The build matrix will ensure the Core Library compiles on all targets on every PR.

| OS | Target | Purpose |
|---|---|---|
| **ubuntu-latest** | `x86_64-unknown-linux-gnu` | Tests, Clippy, Audit (Fastest) |
| **ubuntu-latest** | `aarch64-linux-android` | Android Cross-Compile Check |
| **windows-latest** | `x86_64-pc-windows-msvc` | Windows DLL Build |
| **macos-latest** | `aarch64-apple-ios` | iOS Build |
| **macos-latest** | `aarch64-apple-darwin` | macOS ARM64 Build |

## 3. Pipeline Stages

### Stage 1: Quality Gate (Fast)
- **Triggers**: Push to branch, PR.
- **Jobs**:
  - `cargo fmt -- --check`
  - `cargo clippy -- -D warnings` (Strict Linting)
  - `cargo test --lib` (Unit Tests)

### Stage 2: Build & Bindings (Slow)
- **Triggers**: Push to `main`, Release Tags.
- **Jobs**:
  - **Android**: Build `.so` + Generate Kotlin bindings (UniFFI).
  - **Apple**: Build `XCFramework` (iOS+Sim+Mac) + Swift bindings.
  - **Windows**: Build `.dll`.

### Stage 3: Release
- **Triggers**: Tag `v*`.
- **Jobs**: Upload artifacts to GitHub Releases.
