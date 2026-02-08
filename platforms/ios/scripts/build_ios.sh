#!/bin/bash
set -e

# Setup directories
START_DIR=$(pwd)
CORE_DIR="../../../core"
OUTPUT_DIR="../VoidWarp/Native"
mkdir -p $OUTPUT_DIR

# 1. Install targets (if needed, run this once manually or include checks)
# rustup target add aarch64-apple-ios
# rustup target add aarch64-apple-ios-sim

echo "🚀 Building for iOS Device (aarch64)..."
cd $CORE_DIR
cargo build --release --target aarch64-apple-ios

echo "🚀 Building for iOS Simulator (aarch64)..."
cargo build --release --target aarch64-apple-ios-sim

# echo "🚀 Building for iOS Simulator (x86_64 - legacy)..." 
# cargo build --release --target x86_64-apple-ios

# 2. Create Universal Library (Fat Binary) using lipo
# We combine device (arm64) and simulator (arm64-sim)
# Note: Modern Xcode uses XCFrameworks better, but static lib is simpler for single-file drop-in.

echo "📦 Lipo: Creating Universal Static Library..."
lipo -create \
    target/aarch64-apple-ios/release/libvoidwarp_core.a \
    target/aarch64-apple-ios-sim/release/libvoidwarp_core.a \
    -output "$START_DIR/$OUTPUT_DIR/libvoidwarp_core.a"

# 3. Copy Universal Lib
echo "✅ Created $OUTPUT_DIR/libvoidwarp_core.a"

# 4. Generate C Header (Optional: stick to the manual one we created)
# cbindgen --config cbindgen.toml --crate voidwarp-core --output "$START_DIR/$OUTPUT_DIR/voidwarp_core.h"
