#!/usr/bin/env bash
# Publishes this repo under your GitHub account and prints the curl one-liner.
set -euo pipefail
cd "$(dirname "$0")"

USER_="${1:-}"
if [ -z "$USER_" ]; then
  command -v gh >/dev/null || { echo "install gh, or pass your username: ./publish.sh <user>"; exit 1; }
  USER_="$(gh api user -q .login)"
fi
[ -n "$USER_" ] || { echo "could not determine username"; exit 1; }

echo "publishing as $USER_"
sed -i '' "s|__GH_USER__|$USER_|g" install.sh README.md

git init -q -b main 2>/dev/null || git symbolic-ref HEAD refs/heads/main
git add -A
git commit -q -m "Bozo Executer"
gh repo create bozo-executer --public --source=. --remote=origin --push

echo
echo "install one-liner:"
echo "  curl -fsSL https://raw.githubusercontent.com/$USER_/bozo-executer/main/install.sh | bash"
