#!/usr/bin/env bash
# Bozo Executer — one-line installer for macOS arm64.
#
#   curl -fsSL https://raw.githubusercontent.com/__GH_USER__/bozo-executer/main/install.sh | bash
#
# Env overrides:
#   BOZO_REPO=owner/repo          default __GH_USER__/bozo-executer
#   BOZO_REF=branch-or-tag        default main
#   BOZO_DIR=/path                default ~/bozo-executer
#   BOZO_NO_INJECT=1              build only, skip injection

set -euo pipefail

REPO="${BOZO_REPO:-__GH_USER__/bozo-executer}"
REF="${BOZO_REF:-main}"
DIR="${BOZO_DIR:-$HOME/bozo-executer}"
INJECT="${BOZO_NO_INJECT:+0}"; INJECT="${INJECT:-1}"

if [ -t 1 ]; then R=$'\033[31m'; G=$'\033[32m'; D=$'\033[2m'; Z=$'\033[0m'; else R=; G=; D=; Z=; fi
say() { printf '%s\n' "$*"; }
ok()  { printf '%s ok %s %s\n' "$G" "$Z" "$*"; }
die() { printf '%s !! %s %s\n' "$R" "$Z" "$*" >&2; exit 1; }
usage() {
  cat <<'EOF'
usage: install.sh [--no-inject]

Installs Bozo Executer to ~/bozo-executer, builds the dylib and the TUI,
then injects into a running Roblox client.

  --no-inject    build only
EOF
}

for a in "$@"; do
  case "$a" in
    --no-inject) INJECT=0 ;;
    -h|--help)   usage; exit 0 ;;
    *)           printf 'unknown flag: %s\n' "$a" >&2; exit 2 ;;
  esac
done

say ""
say "  Bozo Executer — installer"
say "  ${D}macOS arm64 · .NET 8 · Roblox${Z}"
say ""

# --- platform -------------------------------------------------------------
[ "$(uname -s)" = "Darwin" ] || die "macOS only."
[ "$(uname -m)" = "arm64"  ] || die "Apple Silicon only. Roblox dropped Intel Mac support."

# --- dependencies ---------------------------------------------------------
command -v git >/dev/null 2>&1 || die "git not found. Run: xcode-select --install"

if ! xcode-select -p >/dev/null 2>&1 || ! command -v clang >/dev/null 2>&1; then
  die "Xcode Command Line Tools missing. Run: xcode-select --install"
fi
ok "command line tools"

command -v dotnet >/dev/null 2>&1 || die "dotnet not found. Install the .NET 8 SDK: https://dot.net/v1/dotnet-install.sh"
if ! dotnet --list-sdks 2>/dev/null | grep -q '^8\.'; then
  die "no .NET 8 SDK found. Installed SDKs:
$(dotnet --list-sdks 2>/dev/null || echo '  none')"
fi
ok "dotnet 8 sdk"

# --- source ---------------------------------------------------------------
if [ -d "$DIR/.git" ]; then
  say "  ${D}updating $DIR${Z}"
  git -C "$DIR" fetch --depth 1 origin "$REF" >/dev/null 2>&1 || die "cannot fetch $REF from $REPO"
  git -C "$DIR" checkout -q --detach FETCH_HEAD || die "cannot check out $REF"
else
  [ -e "$DIR" ] && die "$DIR exists and is not a git checkout. Move it or set BOZO_DIR."
  say "  ${D}cloning $REPO -> $DIR${Z}"
  git clone --depth 1 --branch "$REF" "https://github.com/$REPO.git" "$DIR" >/dev/null 2>&1 \
    || die "clone failed. Is the repo public?"
fi
ok "source at $DIR"

# --- build ----------------------------------------------------------------
say "  ${D}building bozo.dylib${Z}"
bash "$DIR/native/build.sh"
ok "bozo.dylib"

say "  ${D}building bozo${Z}"
dotnet build "$DIR/src/bozo.csproj" -c Release --nologo -v quiet
ok "bozo"

cat > "$DIR/bozo" <<'WRAP'
#!/usr/bin/env bash
exec dotnet "$HOME/bozo-executer/src/bin/Release/net8.0/bozo.dll" "$@"
WRAP
chmod +x "$DIR/bozo"

if [ -d "$HOME/.local/bin" ]; then
  ln -sf "$DIR/bozo" "$HOME/.local/bin/bozo"
  ok "linked ~/.local/bin/bozo"
fi

# --- inject ---------------------------------------------------------------
if [ "$INJECT" = "0" ]; then
  say ""
  ok "done — build only"
  say "  run: $DIR/bozo"
  exit 0
fi

say ""
say "  ${D}injecting into Roblox${Z}"
if pgrep -qf 'RobloxPlayer|/Roblox$' >/dev/null 2>&1; then
  bash "$DIR/inject.sh"
else
  say "  ${R}!${Z} Roblox is not running — launch Roblox, then run:"
  say "      bash $DIR/inject.sh"
  say ""
fi

say ""
ok "done"
say "  run: $DIR/bozo"
say "  ${D}uninstall: rm -rf $DIR${Z}"
