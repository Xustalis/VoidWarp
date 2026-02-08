# VoidWarp iOS Client Setup Guide

Since you are on Windows, you cannot compile the iOS app directly. This package contains **all the source code** needed to build the app on a Mac.

## 📁 Package Contents

- **scripts/build_ios.sh**: Script to compile Rust core into `libvoidwarp_core.a` (Universal Static Library).
- **VoidWarp/Native/voidwarp_core.h**: C-header file bridging Rust FFI to Swift.
- **VoidWarp/Core/VoidWarpEngine.swift**: Main logic engine (Swift).
- **VoidWarp/UI/ContentView.swift**: Main UI implementation (SwiftUI).
- **VoidWarp/VoidWarpApp.swift**: App Entry Point.

## 🛠️ Setup Instructions (On Mac)

1.  **Install Rust & Targets**
    ```bash
    rustup target add aarch64-apple-ios
    rustup target add aarch64-apple-ios-sim
    ```

2.  **Compile Core Library**
    Open Terminal, go to `platforms/ios/scripts` and run:
    ```bash
    chmod +x build_ios.sh
    ./build_ios.sh
    ```
    This will generate `platforms/ios/VoidWarp/Native/libvoidwarp_core.a`.

3.  **Create Xcode Project**
    - Open Xcode -> Create New Project -> **App**.
    - Product Name: `VoidWarp`
    - Interface: `SwiftUI`
    - Language: `Swift`
    - Save it in `platforms/ios` (overwrite/merge with existing folders).

4.  **Add Source Files**
    - Drag `VoidWarp/Core`, `VoidWarp/UI`, `VoidWarp/Native` folders into Xcode Project Navigator.
    - Ensure "Copy items if needed" is unchecked (to keep references).
    - **Crucial**: Delete the default `ContentView.swift` and `VoidWarpApp.swift` created by Xcode, and use the ones provided in this package.

5.  **Configure Bridging Header**
    - Xcode should ask "Would you like to configure an Objective-C bridging header?" -> Click **Yes**.
    - If not manually:
        - Go to Build Settings -> `Objective-C Bridging Header`.
        - Set it to: `VoidWarp/Native/voidwarp_core.h` (or create a file importing it).
        - **Simpler method**: Create a file named `VoidWarp-Bridging-Header.h` and add `#include "voidwarp_core.h"`.

6.  **Link Library**
    - Go to **Build Phases** -> **Link Binary With Libraries**.
    - Drag `Native/libvoidwarp_core.a` here.
    - Add `SystemConfiguration.framework` (often needed for Rust generic networking).

7.  **Info.plist Customization**
    Add these keys to `Info.plist` to allow network discovery:
    - **Privacy - Local Network Usage Description**: "VoidWarp uses local network to discover peers."
    - **Bonjour services**: Add `_voidwarp._tcp` and `_voidwarp._udp`.

8.  **Build & Run**
    Select your iPhone or Simulator and press Play (Cmd+R).

## 🚀 Performance
This iOS client includes the **Streaming Mode** optimization (<32MB files sent as single chunk), ensuring parity with Android and Windows versions.
