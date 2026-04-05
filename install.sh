#!/usr/bin/env bash
set -euo pipefail

OWNER="${OWNER:-JohanWes}"
REPO="${REPO:-Optiscaler-Client-Linux}"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/share/OptiscalerClient}"
BIN_DIR="${BIN_DIR:-$HOME/.local/bin}"
DESKTOP_DIR="${DESKTOP_DIR:-$HOME/.local/share/applications}"
ICON_DIR="${ICON_DIR:-$HOME/.local/share/icons/hicolor/256x256/apps}"
APPIMAGE_NAME="${APPIMAGE_NAME:-OptiscalerClient.AppImage}"
APPIMAGE_LINK_NAME="${APPIMAGE_LINK_NAME:-OptiscalerClient}"
DESKTOP_FILE_NAME="${DESKTOP_FILE_NAME:-OptiscalerClient.desktop}"
ICON_FILE_NAME="${ICON_FILE_NAME:-optiscalerclient.png}"
ICON_URL="${ICON_URL:-https://raw.githubusercontent.com/${OWNER}/${REPO}/main/assets/icon.png}"

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

mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$DESKTOP_DIR" "$ICON_DIR"

tmp_file="$(mktemp)"
trap 'rm -f "$tmp_file"' EXIT

curl -fsSL "$download_url" -o "$tmp_file"
install -m 755 "$tmp_file" "$INSTALL_DIR/$APPIMAGE_NAME"
cat > "$BIN_DIR/$APPIMAGE_LINK_NAME" <<EOF
#!/usr/bin/env bash
set -euo pipefail
exec "$INSTALL_DIR/$APPIMAGE_NAME" "\$@"
EOF
chmod 755 "$BIN_DIR/$APPIMAGE_LINK_NAME"

cat > "$DESKTOP_DIR/$DESKTOP_FILE_NAME" <<EOF
[Desktop Entry]
Type=Application
Name=OptiScaler Client
Exec=$BIN_DIR/$APPIMAGE_LINK_NAME
Icon=optiscalerclient
Categories=Utility;Game;
Terminal=false
EOF

curl -fsSL "$ICON_URL" -o "$ICON_DIR/$ICON_FILE_NAME"

if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database "$DESKTOP_DIR" >/dev/null 2>&1 || true
fi

echo "Installed ${APPIMAGE_NAME} to ${INSTALL_DIR}"
echo "Launcher: ${BIN_DIR}/${APPIMAGE_LINK_NAME}"
echo "Desktop entry: ${DESKTOP_DIR}/${DESKTOP_FILE_NAME}"
