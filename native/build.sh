#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")"
clang++ -std=c++17 -dynamiclib -O2 -arch arm64 \
  -framework Foundation -o bozo.dylib bozo.dylib.cpp
codesign --force --sign - bozo.dylib
