#!/usr/bin/env bash
# Bozo Key Approval — prebuilt macOS installer
set -euo pipefail

REPO="ttmeticks-source/executerrr"
DIR="${BOZO_DIR:-$HOME/bozo-key-approval}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

say() { printf '%s\n' "$*"; }
die() { printf 'Error: %s\n' "$*" >&2; exit 1; }

[ "$(uname -s)" = "Darwin" ] || die "This installer is for macOS."
command -v curl >/dev/null 2>&1 || die "curl is required."
command -v tar >/dev/null 2>&1 || die "tar is required."

MODEL="$(sysctl -n hw.model 2>/dev/null || printf 'Unknown Mac')"
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null || printf '0')" = "1" ]; then
  ASSET="bozo-macos-apple-silicon.tar.gz"
  ARCH="Apple Silicon"
else
  ASSET="bozo-macos-intel.tar.gz"
  ARCH="Intel"
fi

[ ! -e "$DIR" ] || die "$DIR already exists. Move it or set BOZO_DIR to another path."

say "Detected: $MODEL ($ARCH)"
say "Downloading the prebuilt app..."
URL="https://github.com/$REPO/releases/latest/download/$ASSET"
curl -fL --retry 3 "$URL" -o "$TMP/bozo.tar.gz"

mkdir -p "$TMP/app"
tar -xzf "$TMP/bozo.tar.gz" -C "$TMP/app"
[ -f "$TMP/app/bozo" ] || die "The downloaded release did not contain the app."

mkdir -p "$DIR" "$HOME/.local/bin" "$HOME/Library/Application Support/bozo"
install -m 755 "$TMP/app/bozo" "$DIR/bozo"
cat > "$DIR/bozo-launcher" <<WRAPPER
#!/usr/bin/env bash
exec "$DIR/bozo" "\$@"
WRAPPER
chmod 755 "$DIR/bozo-launcher"
ln -sf "$DIR/bozo-launcher" "$HOME/.local/bin/bozo"

CONFIG="$HOME/Library/Application Support/bozo/config.env"
if [ ! -f "$CONFIG" ]; then
  cat > "$CONFIG" <<'CONFIG'
# Keep this file private. Never commit your bot token.
DISCORD_BOT_TOKEN=
DISCORD_CHANNEL_ID=
CONFIG
  chmod 600 "$CONFIG"
fi

say "Installed the prebuilt $ARCH version."
say "Run: $DIR/bozo"
say "Discord config: $CONFIG"
say "No .NET SDK or build tools are needed on this Mac."
