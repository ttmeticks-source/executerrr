#!/usr/bin/env bash
# Bozo Key Approval — macOS installer
set -euo pipefail

DIR="${BOZO_DIR:-$HOME/bozo-key-approval}"
REPO="${BOZO_REPO:-ttmeticks-source/executerrr}"
REF="${BOZO_REF:-safe-key-approval-app}"

say() { printf '%s\n' "$*"; }
die() { printf 'Error: %s\n' "$*" >&2; exit 1; }

[ "$(uname -s)" = "Darwin" ] || die "macOS only."
command -v git >/dev/null 2>&1 || die "git is required."
command -v dotnet >/dev/null 2>&1 || die ".NET 8 SDK is required (https://dot.net)."

if [ -d "$DIR/.git" ]; then
  git -C "$DIR" fetch origin "$REF"
  git -C "$DIR" checkout "$REF"
  git -C "$DIR" pull --ff-only origin "$REF"
else
  [ ! -e "$DIR" ] || die "$DIR already exists and is not a Git checkout."
  git clone --depth 1 --branch "$REF" "https://github.com/$REPO.git" "$DIR"
fi

dotnet publish "$DIR/bozo.csproj" -c Release -o "$DIR/publish"
mkdir -p "$HOME/.local/bin" "$HOME/Library/Application Support/bozo"
cat > "$DIR/bozo" <<WRAPPER
#!/usr/bin/env bash
exec dotnet "$DIR/publish/bozo.dll" "\$@"
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

say "Installed. Run: $DIR/bozo"
say "Configure Discord in: $CONFIG"
say "The bot needs View Channel, Send Messages, Embed Links, and Use External/Message Components permissions."
