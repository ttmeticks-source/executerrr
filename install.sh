#!/usr/bin/env bash
# Bozo Key Approval — macOS one-line installer
set -euo pipefail

REPO="ttmeticks-source/executerrr"
REF="safe-key-approval-app"
DIR="${BOZO_DIR:-$HOME/bozo-key-approval}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

say() { printf '%s\n' "$*"; }
die() { printf 'Error: %s\n' "$*" >&2; exit 1; }

[ "$(uname -s)" = "Darwin" ] || die "This installer is for macOS."
command -v curl >/dev/null 2>&1 || die "curl is required."

MODEL="$(sysctl -n hw.model 2>/dev/null || printf 'Unknown Mac')"
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null || printf '0')" = "1" ]; then
  RID="osx-arm64"
  ARCH="Apple Silicon"
else
  RID="osx-x64"
  ARCH="Intel"
fi

say "Detected: $MODEL ($ARCH)"
say "Downloading the matching $RID build source..."

ARCHIVE="$TMP/source.tar.gz"
curl -fL --retry 3 "https://codeload.github.com/$REPO/tar.gz/refs/heads/$REF" -o "$ARCHIVE"
mkdir -p "$TMP/source"
tar -xzf "$ARCHIVE" --strip-components=1 -C "$TMP/source"

command -v dotnet >/dev/null 2>&1 || die ".NET 8 SDK is required to build this version. Install it from https://dot.net and run this command again."

[ ! -e "$DIR" ] || die "$DIR already exists. Move it or set BOZO_DIR to another path."
mkdir -p "$DIR"
cp -R "$TMP/source/." "$DIR/"

dotnet publish "$DIR/bozo.csproj" -c Release -r "$RID" --self-contained true -o "$DIR/publish"
mkdir -p "$HOME/.local/bin" "$HOME/Library/Application Support/bozo"
cat > "$DIR/bozo" <<WRAPPER
#!/usr/bin/env bash
exec "$DIR/publish/bozo" "\$@"
WRAPPER
chmod +x "$DIR/bozo"
ln -sf "$DIR/bozo" "$HOME/.local/bin/bozo"

CONFIG="$HOME/Library/Application Support/bozo/config.env"
if [ ! -f "$CONFIG" ]; then
  cat > "$CONFIG" <<'CONFIG'
# Keep this file private. Never commit your bot token.
DISCORD_BOT_TOKEN=
DISCORD_CHANNEL_ID=
CONFIG
  chmod 600 "$CONFIG"
fi

say "Installed for $RID."
say "Run: $DIR/bozo"
say "Discord config: $CONFIG"
say "The bot needs View Channel, Send Messages, Embed Links, and Use External/Message Components permissions."
