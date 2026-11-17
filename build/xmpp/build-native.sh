#!/usr/bin/env bash
set -euo pipefail

runtime="${1:?Pass a HISA runtime identifier}"
repo="$(cd "$(dirname "$0")/../.." && pwd)"
destination="${2:-$repo/build/xmpp-native/$runtime}"
mkdir -p "$destination"
destination="$(cd "$destination" && pwd)"
temp_root="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
scratch="$(mktemp -d "$temp_root/hisa-xmpp.XXXXXXXX")"
case "$scratch" in
  "$temp_root"/hisa-xmpp.*) trap 'rm -rf -- "$scratch"' EXIT ;;
  *) exit 1 ;;
esac

strophe_version=0.14.0
openssl_version=3.5.9
expat_version=2.10.0
jobs="${HISA_XMPP_BUILD_JOBS:-4}"
prefix="$scratch/dependencies"
compiler="${CC:-cc}"
extra_cflags=-fPIC
case "$runtime" in
  win-x64) openssl_target=mingw64; extra_cflags=-DXML_STATIC ;;
  linux-x64|linux-musl-x64) openssl_target=linux-x86_64 ;;
  linux-arm64) openssl_target=linux-aarch64 ;;
  osx-x64) openssl_target=darwin64-x86_64-cc ;;
  osx-arm64) openssl_target=darwin64-arm64-cc ;;
  *) echo "Unsupported XMPP runtime $runtime" >&2; exit 1 ;;
esac

fetch_source() {
  local name="$1" url="$2" checksum="$3" archive="$scratch/$1"
  if [[ -n "${HISA_XMPP_SOURCE_CACHE:-}" && -f "$HISA_XMPP_SOURCE_CACHE/$name" ]]; then
    cp "$HISA_XMPP_SOURCE_CACHE/$name" "$archive"
  else
    curl --fail --silent --show-error --location --retry 3 "$url" --output "$archive"
  fi
  python3 - "$archive" "$checksum" <<'PY'
import hashlib
import sys

with open(sys.argv[1], "rb") as archive:
    if hashlib.sha256(archive.read()).hexdigest() != sys.argv[2]:
        raise SystemExit("The XMPP dependency archive checksum did not match.")
PY
  tar xzf "$archive" -C "$scratch"
}

fetch_source "libstrophe-$strophe_version.tar.gz" \
  "https://github.com/strophe/libstrophe/releases/download/$strophe_version/libstrophe-$strophe_version.tar.gz" \
  d079668474d5c3aa4555347c33e77014a1071629603557cc506a6bc6f82e01f5
fetch_source "openssl-$openssl_version.tar.gz" \
  "https://github.com/openssl/openssl/releases/download/openssl-$openssl_version/openssl-$openssl_version.tar.gz" \
  603f5602e2eef00d77fbd429d34dcd5822bb301757a1bc9cdb24c670f1eb859a
fetch_source "expat-$expat_version.tar.gz" \
  "https://github.com/libexpat/libexpat/releases/download/R_${expat_version//./_}/expat-$expat_version.tar.gz" \
  5466a341f9c692ef7a958a6881311be597f8e1ce345984cf78414f54793dea4c

strophe="$scratch/libstrophe-$strophe_version"
openssl="$scratch/openssl-$openssl_version"
expat="$scratch/expat-$expat_version"
python3 "$repo/build/xmpp/harden-libstrophe.py" "$strophe"

# The release recipes pin and link the same OpenSSL LTS and Expat release
# for every runtime. Build them without shared modules or applications.
(
  cd "$openssl"
  perl Configure "$openssl_target" --prefix="$prefix" --libdir=lib --openssldir=/etc/ssl \
    no-shared no-module no-dso no-engine no-tests no-apps no-quic
  make -j"$jobs" build_libs
  make install_dev
)
(
  cd "$expat"
  CFLAGS="-O2 $extra_cflags" ./configure --prefix="$prefix" --libdir="$prefix/lib" \
    --enable-static --disable-shared --without-docbook --without-xmlwf --without-examples --without-tests
  make -j"$jobs"
  make install
)

cd "$strophe"
# Use upstream's compiled objects and link the private library directly.
# Libtool's MinGW shared-library mode drops static inputs.
openssl_CFLAGS="-I$prefix/include" openssl_LIBS="-L$prefix/lib -lssl -lcrypto" \
expat_CFLAGS="-I$prefix/include" expat_LIBS="-L$prefix/lib -lexpat" \
CFLAGS="${CFLAGS:--O2} -std=gnu11 -Dtls_free=hisa_strophe_tls_free $extra_cflags" \
./configure --disable-shared --enable-static --disable-examples --disable-zlib
make -j"$jobs" libstrophe.la

dependencies=("$prefix/lib/libssl.a" "$prefix/lib/libcrypto.a" "$prefix/lib/libexpat.a")
case "$runtime" in
  win-x64)
    output="$destination/hisa-strophe.dll"
    {
      printf 'EXPORTS\n'
      nm --defined-only .libs/libstrophe.a | awk '$2 == "T" && $3 ~ /^xmpp_/ { print $3 }'
    } > "$scratch/exports.def"
    "$compiler" -shared -static-libgcc "$scratch/exports.def" \
      -Wl,--whole-archive .libs/libstrophe.a -Wl,--no-whole-archive "${dependencies[@]}" \
      -lws2_32 -lgdi32 -lcrypt32 -lbcrypt -o "$output"
    imports="$(objdump -p "$output" | sed -n 's/.*DLL Name: //p')"
    if printf '%s\n' "$imports" | grep -Eiq 'lib(ssl|crypto|expat|gcc|winpthread)|msys'; then
      echo "An unexpected Windows XMPP runtime dependency was linked: $imports" >&2
      exit 1
    fi
    "$compiler" "$repo/build/xmpp/native-smoke.c" -o "$scratch/native-smoke.exe"
    "$scratch/native-smoke.exe" "$(cygpath -w "$output")"
    ;;
  linux-*)
    output="$destination/libhisa-strophe.so"
    printf '{ global: xmpp_*; local: *; };\n' > "$scratch/exports.map"
    "$compiler" -shared -Wl,-z,defs -Wl,--version-script,"$scratch/exports.map" \
      -Wl,--whole-archive .libs/libstrophe.a -Wl,--no-whole-archive "${dependencies[@]}" \
      -ldl -lresolv -pthread -o "$output"
    "$compiler" "$repo/build/xmpp/native-smoke.c" -ldl -o "$scratch/native-smoke"
    "$scratch/native-smoke" "$output"
    ;;
  osx-*)
    output="$destination/libhisa-strophe.dylib"
    nm -gU .libs/libstrophe.a | awk '$2 == "T" && $3 ~ /^_xmpp_/ { print $3 }' > "$scratch/exports.symbols"
    "$compiler" -dynamiclib -Wl,-force_load,.libs/libstrophe.a \
      -Wl,-exported_symbols_list,"$scratch/exports.symbols" "${dependencies[@]}" \
      -lresolv -pthread -install_name '@loader_path/libhisa-strophe.dylib' -o "$output"
    codesign --force --sign - "$output"
    "$compiler" "$repo/build/xmpp/native-smoke.c" -o "$scratch/native-smoke"
    "$scratch/native-smoke" "$output"
    ;;
esac
cp "$strophe/MIT-LICENSE.txt" "$destination/libstrophe-LICENSE.txt"
cp "$openssl/LICENSE.txt" "$destination/openssl-LICENSE.txt"
cp "$expat/COPYING" "$destination/expat-LICENSE.txt"
printf 'libstrophe %s\nOpenSSL %s\nExpat %s\n' "$strophe_version" "$openssl_version" "$expat_version" > "$destination/versions.txt"

