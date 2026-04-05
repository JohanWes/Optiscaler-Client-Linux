#!/usr/bin/env bash
set -euo pipefail

OWNER="${OWNER:-JohanWes}"
REPO="${REPO:-Optiscaler-Client-Linux}"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/share/OptiscalerClient}"
BIN_DIR="${BIN_DIR:-$HOME/.local/bin}"
APPIMAGE_NAME="${APPIMAGE_NAME:-OptiscalerClient.AppImage}"
APPIMAGE_LINK_NAME="${APPIMAGE_LINK_NAME:-OptiscalerClient}"

api_url="https://api.github.com/repos/${OWNER}/${REPO}/releases/latest"

download_url="$(
  curl -fsSL -H 'Accept: application/vnd.github+json' "$api_url" \
    | grep -Eo '"browser_download_url":[[:space:]]*"[^"]+\.AppImage"' \
    | head -n1 \
    | sed -E 's/^"browser_download_url":[[:space:]]*"([^"]+)"$/\1/'
)"

if [ -z "${download_url}" ]; then
  echo "Could not find an AppImage asset in the latest release for ${OWNER}/${REPO}." >&2
  exit 1
fi

mkdir -p "$INSTALL_DIR" "$BIN_DIR"

tmp_file="$(mktemp)"
trap 'rm -f "$tmp_file"' EXIT

curl -fsSL "$download_url" -o "$tmp_file"
install -m 755 "$tmp_file" "$INSTALL_DIR/$APPIMAGE_NAME"
ln -sf "$INSTALL_DIR/$APPIMAGE_NAME" "$BIN_DIR/$APPIMAGE_LINK_NAME"

echo "Installed ${APPIMAGE_NAME} to ${INSTALL_DIR}"
echo "Launcher: ${BIN_DIR}/${APPIMAGE_LINK_NAME}"
