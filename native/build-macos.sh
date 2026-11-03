#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
rustup target add x86_64-apple-darwin aarch64-apple-darwin
cargo build --locked --release --target x86_64-apple-darwin
cargo build --locked --release --target aarch64-apple-darwin
mkdir -p ../Libraries/macOS
lipo -create target/x86_64-apple-darwin/release/libmultiplayer_stacktrace.dylib \
    target/aarch64-apple-darwin/release/libmultiplayer_stacktrace.dylib \
    -output ../Libraries/macOS/libmultiplayer_stacktrace.dylib
install_name_tool -id '@rpath/libmultiplayer_stacktrace.dylib' ../Libraries/macOS/libmultiplayer_stacktrace.dylib
codesign --force --sign - ../Libraries/macOS/libmultiplayer_stacktrace.dylib
