# VoidWarp  🌌

[![CI](https://github.com/XenithCode/VoidWarp/actions/workflows/ci.yml/badge.svg)](https://github.com/XenithCode/VoidWarp/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

**VoidWarp** 是一个高性能、跨平台的局域网安全文件传输工具。它旨在提供更广泛的设备支持，同时保持极高的传输速度和安全性。

与常见的文件传输工具不同，VoidWarp 采用了 **Hybrid Core (混合核心)** 架构：
*   **核心层 (Rust)**: 处理所有网络传输、加密、设备发现和文件 I/O。不仅保证了内存安全，还能在所有平台上提供一致的高性能。
*   **UI 层 (Native)**: 使用各平台原生技术（WPF for Windows, Jetpack Compose for Android, SwiftUI for iOS/macOS）构建，确保最佳的用户体验和系统集成。

## ✨ 主要功能 (Features)

*   **🚀 极速传输**: 基于 UDP 的自定义协议 (VWTP)，内置拥塞控制和重传机制，最大限度利用局域网带宽。
*   **🔒 端到端加密**: 采用 ECDH 密钥交换和 AES-256 加密，确保传输内容绝不泄露。
*   **🔍 自动发现**: mDNS + 多网卡 UDP 广播，零配置发现周围设备（含 Android 与 Windows 互发现）。
*   **📱 全平台支持**:
    *   **Windows**: WPF 现代化暗色主题界面，支持 mDNS + UDP 广播发现、ZIP/安装包分发
    *   **Android**: Jetpack Compose 原生体验
    *   **macOS / iOS**: 计划中

## 🛠️ 安装与使用 (Usage)

### Windows 用户（推荐）

*   **安装包**：从 [Releases](https://github.com/XenithCode/VoidWarp/releases) 下载 `VoidWarp-Windows-x64-Setup.exe`或 `VoidWarp-Windows-x64.zip`，解压后运行 `VoidWarp.Windows.exe`，或运行 `install.bat` 安装到程序目录并创建快捷方式。
*   **若 Android 扫描不到本机**：以管理员身份运行解压目录下的 `setup_firewall.bat` 配置防火墙。

### 📥 源码构建 (Build from Source)

需要安装以下环境:
*   **Rust Toolchain**: 最新 Stable 版本 (`rustup update`)
*   **Windows**: Visual Studio 2022 (带 .NET Desktop Development 和 C++ build tools)
*   **Android**: Android Studio (带 NDK)

#### 1. 克隆项目
```bash
git clone https://github.com/XenithCode/VoidWarp.git
cd VoidWarp
```

#### 2. 构建 Windows 客户端
```bash
# 构建 Rust 核心库
cd core
cargo build --release

# 构建 Windows WPF 应用
cd ../platforms/windows
dotnet build -c Release
```
运行 `bin/Release/net8.0-windows/VoidWarp.Windows.exe` 即可启动。

#### 3. 生成 Windows 安装包
在项目根目录执行 `publish_windows.bat`，将生成 `publish\VoidWarp-Windows` 文件夹和 `publish\VoidWarp-Windows-x64.zip`。若已安装 Inno Setup 6，可再运行：
```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" platforms\windows\installer\VoidWarp.iss
```
得到 `publish\output\VoidWarp-Windows-x64-Setup.exe` 单文件安装包。

#### 4. 构建 Android 客户端
需配置 NDK 与 Rust 目标 `aarch64-linux-android` 等，在项目根目录执行 `build_android.bat`（或进入 `platforms/android` 运行 `gradlew assembleDebug`）。

## 📜 许可证 (License)

Copyright © 2026 Xustalis.

本项目采用 **GNU General Public License v3.0 (GPLv3)** 许可证。

*   ✅ **自由使用**: 您可以自由下载、运行和使用本软件。
*   ✅ **源码共享**: 您可以获取、学习并修改本项目源码。
*   ⚠️ **分发要求**: 如果您分发本软件或其修改版本，必须以相同的许可证 (GPLv3) 公开您的源代码。
*   ❌ **闭源商业**: 严禁将本项目源码集成到任何闭源商业软件中。

我们希望通过开源协作共同完善 VoidWarp。如果您有其他授权需求，请联系作者。

🌟 欢迎贡献与互动:

*    VoidWarp 的成长离不开你的支持！

*    如果你觉得这个项目不错：请点击右上角的 Star ⭐，这是最大的鼓励！

*    如果你有好的想法：欢迎 Fork 并在自己的分支上尝试。

*    如果你发现了 Bug：请毫不犹豫地提交 Issue 或发起 Pull Request !!!

---

## 🏗️ 架构设计

```mermaid
graph TD
    A["UI Layer (WPF/Compose/SwiftUI)"] <-->|FFI / JNI| B["VoidWarp Core (Rust)"]
    B --> C["Discovery (mDNS)"]
    B --> D["Transport (UDP/VWTP)"]
    B --> E["Security (Ring/AES)"]
    B --> F["File I/O"]
```

详细协议文档请参阅 [docs/protocol/PROTOCOL_SPEC.md](docs/protocol/PROTOCOL_SPEC.md)。
