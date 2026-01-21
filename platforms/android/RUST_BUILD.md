# Rust Cross-Compilation for Android

## Prerequisites
1. Install Android NDK via Android Studio SDK Manager
2. Add Rust Android targets:
   ```bash
   rustup target add aarch64-linux-android
   rustup target add armv7-linux-androideabi
   rustup target add x86_64-linux-android
   ```

## Configuration
Create or update `~/.cargo/config.toml`:

```toml
[target.aarch64-linux-android]
linker = "C:/Users/YOUR_USER/AppData/Local/Android/Sdk/ndk/VERSION/toolchains/llvm/prebuilt/windows-x86_64/bin/aarch64-linux-android26-clang.cmd"

[target.armv7-linux-androideabi]
linker = "C:/Users/YOUR_USER/AppData/Local/Android/Sdk/ndk/VERSION/toolchains/llvm/prebuilt/windows-x86_64/bin/armv7a-linux-androideabi26-clang.cmd"

[target.x86_64-linux-android]
linker = "C:/Users/YOUR_USER/AppData/Local/Android/Sdk/ndk/VERSION/toolchains/llvm/prebuilt/windows-x86_64/bin/x86_64-linux-android26-clang.cmd"
```

## Build Commands
```bash
# Build for ARM64 (most modern devices)
cargo build --release --target aarch64-linux-android

# Build for ARMv7 (older devices)
cargo build --release --target armv7-linux-androideabi

# Build for x86_64 (emulators)
cargo build --release --target x86_64-linux-android
```

## Output Location
The compiled `.so` files will be in:
- `target/aarch64-linux-android/release/libvoidwarp_core.so`
- `target/armv7-linux-androideabi/release/libvoidwarp_core.so`
- `target/x86_64-linux-android/release/libvoidwarp_core.so`

Copy these to:
- `platforms/android/app/src/main/jniLibs/arm64-v8a/libvoidwarp_core.so`
- `platforms/android/app/src/main/jniLibs/armeabi-v7a/libvoidwarp_core.so`
- `platforms/android/app/src/main/jniLibs/x86_64/libvoidwarp_core.so`
