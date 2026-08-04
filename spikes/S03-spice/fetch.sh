#!/usr/bin/env bash
set -euo pipefail

# Fetch SPICE spike artifacts (kernels + CSPICE source) from GitHub mirrors.
# NAIF hosts (naif.jpl.nasa.gov) are unreachable from the dev network; mirrors
# cross-checked by sha256. NAIF-official checksum verification deferred to the
# Coolify host network check (S0.11).

KERNEL_DIR="$(dirname "$0")/fixtures/kernels"
VENDOR_DIR="$(dirname "$0")/vendor"
DE440S_SHA256="c1c7feeab882263fc493a9d5a5b2ddd71b54826cdf65d8d17a76126b260a49f2"

mkdir -p "$KERNEL_DIR" "$VENDOR_DIR"

if [ ! -f "$KERNEL_DIR/de440s.bsp" ]; then
  echo "fetching de440s.bsp (32.7 MB) from mirror A (arturania/cspice) ..."
  curl -fsSL --max-time 600 -o "$KERNEL_DIR/de440s.bsp" \
    "https://raw.githubusercontent.com/arturania/cspice/master/kernels/spk/de440s.bsp"
fi

actual=$(shasum -a 256 "$KERNEL_DIR/de440s.bsp" | awk '{print $1}')
if [ "$actual" != "$DE440S_SHA256" ]; then
  echo "ERROR: de440s.bsp sha256 mismatch: $actual (expected $DE440S_SHA256)" >&2
  exit 1
fi
echo "de440s.bsp sha256 OK: $actual"

if [ ! -d "$VENDOR_DIR/cspice" ]; then
  echo "cloning CSPICE source mirror (arturania/cspice, sparse) ..."
  git clone --depth 1 --filter=blob:none --sparse \
    https://github.com/arturania/cspice.git "$VENDOR_DIR/cspice"
  git -C "$VENDOR_DIR/cspice" sparse-checkout set src kernels
fi
echo "cspice source present at $VENDOR_DIR/cspice"
