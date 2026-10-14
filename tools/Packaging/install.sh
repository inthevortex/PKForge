#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/pkforge-app"
APPS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
ICONS_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor/256x256/apps"
BIN_DIR="${HOME}/.local/bin"

if [[ "${1:-}" == "--uninstall" ]]; then
    echo "Uninstalling PKForge..."
    rm -rf "${APP_DIR}"
    rm -f "${APPS_DIR}/pkforge.desktop"
    rm -f "${ICONS_DIR}/pkforge.png"
    rm -f "${BIN_DIR}/pkforge"
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database "${APPS_DIR}" || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -t "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor" || true
    fi
    echo "PKForge uninstalled."
    exit 0
fi

echo "Installing PKForge to ${APP_DIR}..."

mkdir -p "${APP_DIR}" "${APPS_DIR}" "${ICONS_DIR}" "${BIN_DIR}"

# Copy binary and associated assets
if [[ -d "${SCRIPT_DIR}/pkforge" ]]; then
    cp -r "${SCRIPT_DIR}/pkforge/"* "${APP_DIR}/"
else
    echo "Error: pkforge directory not found in ${SCRIPT_DIR}" >&2
    exit 1
fi
chmod +x "${APP_DIR}/PKForge"

# Install icon
if [[ -f "${SCRIPT_DIR}/pkforge.png" ]]; then
    cp "${SCRIPT_DIR}/pkforge.png" "${APP_DIR}/pkforge.png"
    cp "${SCRIPT_DIR}/pkforge.png" "${ICONS_DIR}/pkforge.png"
fi

# Quotes a path for Exec= so spaces keep it one argument. The desktop entry spec
# unescapes strings before it unquotes, so a literal \ becomes \\\\, and " ` $
# become \\" \\` \\$; % is doubled because it starts a field code.
desktop_exec_quote() {
    printf '"%s"' "$(printf '%s' "$1" | sed -e 's/\\/\\\\\\\\/g' -e 's/["`$]/\\\\&/g' -e 's/%/%%/g')"
}

# Install desktop entry
cat <<EOF > "${APPS_DIR}/pkforge.desktop"
[Desktop Entry]
Name=PKForge
Comment=Pokémon save file editor
Exec=$(desktop_exec_quote "${APP_DIR}/PKForge")
Icon=${ICONS_DIR}/pkforge.png
Terminal=false
Type=Application
Categories=Game;
Keywords=pokemon;pkhex;save;editor;
StartupWMClass=PKForge
EOF
chmod +x "${APPS_DIR}/pkforge.desktop"

# Symlink executable to ~/.local/bin
ln -sf "${APP_DIR}/PKForge" "${BIN_DIR}/pkforge"

# Refresh desktop and icon caches
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "${APPS_DIR}" || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -t "${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor" || true
fi

echo "PKForge installed successfully!"
echo "• App binary: ${APP_DIR}/PKForge"
echo "• Desktop entry: ${APPS_DIR}/pkforge.desktop"
echo "• CLI command: ${BIN_DIR}/pkforge"
echo "• To uninstall later, run: ./install.sh --uninstall"
