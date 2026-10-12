#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

VERSION="${1:-3.1.0}"
VERSION="${VERSION#v}"
VERSION="${VERSION//\//-}"

REPO="${2:-sofianeelhor/PKForge}"

ARCH="linux-x64"
OUTPUT_DIR="${ROOT_DIR}/artifacts"
STAGE_DIR="${OUTPUT_DIR}/stage-pkforge"
COMMON_ROOT="${OUTPUT_DIR}/stage-common"

echo "Building and packaging PKForge v${VERSION} (${ARCH}, repo: ${REPO})..."

rm -rf "${STAGE_DIR}" "${COMMON_ROOT}"
mkdir -p "${STAGE_DIR}/pkforge" "${OUTPUT_DIR}"

dotnet publish "${ROOT_DIR}/src/PKForge.App/PKForge.App.csproj" \
    -f net11.0 \
    -c Release \
    -r "${ARCH}" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PKForgeUpdateRepository="${REPO}" \
    -o "${STAGE_DIR}/pkforge"

rm -f "${STAGE_DIR}/pkforge/"*.pdb
chmod +x "${STAGE_DIR}/pkforge/PKForge"

# --- 1. Standalone Tarball ---
TARBALL_DIR="${OUTPUT_DIR}/pkforge-${VERSION}-${ARCH}"
rm -rf "${TARBALL_DIR}"
mkdir -p "${TARBALL_DIR}"
cp -r "${STAGE_DIR}/pkforge" "${TARBALL_DIR}/"
cp "${SCRIPT_DIR}/pkforge.desktop" "${TARBALL_DIR}/pkforge.desktop"
cp "${SCRIPT_DIR}/install.sh" "${TARBALL_DIR}/install.sh"
cp "${ROOT_DIR}/src/PKForge.App/Resources/AppIcon/pkforge.png" "${TARBALL_DIR}/pkforge.png"
chmod +x "${TARBALL_DIR}/install.sh"

TARBALL="${OUTPUT_DIR}/pkforge-${VERSION}-${ARCH}.tar.gz"
echo "Creating ${TARBALL}..."
tar -czf "${TARBALL}" -C "${OUTPUT_DIR}" "pkforge-${VERSION}-${ARCH}"
rm -rf "${TARBALL_DIR}"

# --- Common /usr File Hierarchy for Distro Packages ---
mkdir -p "${COMMON_ROOT}/usr/lib/pkforge"
mkdir -p "${COMMON_ROOT}/usr/bin"
mkdir -p "${COMMON_ROOT}/usr/share/applications"
mkdir -p "${COMMON_ROOT}/usr/share/icons/hicolor/256x256/apps"

cp -a "${STAGE_DIR}/pkforge/"* "${COMMON_ROOT}/usr/lib/pkforge/"
ln -sf /usr/lib/pkforge/PKForge "${COMMON_ROOT}/usr/bin/pkforge"
cp "${SCRIPT_DIR}/pkforge.desktop" "${COMMON_ROOT}/usr/share/applications/pkforge.desktop"
cp "${ROOT_DIR}/src/PKForge.App/Resources/AppIcon/pkforge.png" "${COMMON_ROOT}/usr/share/icons/hicolor/256x256/apps/pkforge.png"

# --- 2. Debian Package (.deb) ---
DEB_DIR="${OUTPUT_DIR}/stage-deb"
DEB_FILE="${OUTPUT_DIR}/pkforge_${VERSION}_amd64.deb"
echo "Creating ${DEB_FILE}..."
rm -rf "${DEB_DIR}"
mkdir -p "${DEB_DIR}/DEBIAN"
cp -a "${COMMON_ROOT}/usr" "${DEB_DIR}/"

INSTALLED_SIZE=$(du -sk "${DEB_DIR}/usr" | cut -f1)
cat <<EOF > "${DEB_DIR}/DEBIAN/control"
Package: pkforge
Version: ${VERSION}
Section: utils
Priority: optional
Architecture: amd64
Installed-Size: ${INSTALLED_SIZE}
Maintainer: Sofiane Elhor <sofianeelhor@gmail.com>
Homepage: https://github.com/${REPO}
Description: Pokémon save file editor
 Cross-platform Pokémon save editor built on PKHeX.Core.
EOF

if command -v dpkg-deb >/dev/null 2>&1; then
    dpkg-deb --build --root-owner-group "${DEB_DIR}" "${DEB_FILE}"
else
    (
        cd "${DEB_DIR}"
        echo "2.0" > debian-binary
        tar -czf control.tar.gz -C DEBIAN .
        tar -czf data.tar.gz -C . usr
        ar -rc "${DEB_FILE}" debian-binary control.tar.gz data.tar.gz
    )
fi
rm -rf "${DEB_DIR}"

# --- 3. Arch Linux Pacman Package (.pkg.tar.zst) ---
PACMAN_DIR="${OUTPUT_DIR}/stage-pacman"
PACMAN_FILE="${OUTPUT_DIR}/pkforge-${VERSION}-1-x86_64.pkg.tar.zst"
echo "Creating ${PACMAN_FILE}..."
rm -rf "${PACMAN_DIR}"
mkdir -p "${PACMAN_DIR}"
cp -a "${COMMON_ROOT}/usr" "${PACMAN_DIR}/"

INSTALLED_BYTES=$(du -sb "${PACMAN_DIR}/usr" | cut -f1)
BUILD_DATE=$(date +%s)
cat <<EOF > "${PACMAN_DIR}/.PKGINFO"
pkgname = pkforge
pkgbase = pkforge
pkgver = ${VERSION}-1
pkgdesc = Pokémon save file editor
url = https://github.com/${REPO}
builddate = ${BUILD_DATE}
packager = PKForge Authors
size = ${INSTALLED_BYTES}
arch = x86_64
license = GPL3
depend = glibc
EOF

tar --zstd -cf "${PACMAN_FILE}" -C "${PACMAN_DIR}" .PKGINFO usr
rm -rf "${PACMAN_DIR}"

# --- 4. Red Hat / RPM Package (.rpm) ---
RPM_FILE="${OUTPUT_DIR}/pkforge-${VERSION}-1.x86_64.rpm"
if command -v rpmbuild >/dev/null 2>&1; then
    echo "Creating ${RPM_FILE}..."
    RPM_BUILD_DIR="${OUTPUT_DIR}/stage-rpm"
    rm -rf "${RPM_BUILD_DIR}"
    mkdir -p "${RPM_BUILD_DIR}"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

    cat <<EOF > "${RPM_BUILD_DIR}/SPECS/pkforge.spec"
%define _binary_payload w19.zstdio
Name:           pkforge
Version:        ${VERSION}
Release:        1%{?dist}
Summary:        Pokémon save file editor
License:        GPL-3.0-or-later
URL:            https://github.com/${REPO}
AutoReqProv:    no

%description
Cross-platform Pokémon save editor built on PKHeX.Core.

%install
mkdir -p %{buildroot}/usr/lib/pkforge
mkdir -p %{buildroot}/usr/bin
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons/hicolor/256x256/apps

cp -a "${COMMON_ROOT}/usr/lib/pkforge/"* %{buildroot}/usr/lib/pkforge/
ln -sf /usr/lib/pkforge/PKForge %{buildroot}/usr/bin/pkforge
cp -a "${COMMON_ROOT}/usr/share/applications/pkforge.desktop" %{buildroot}/usr/share/applications/
cp -a "${COMMON_ROOT}/usr/share/icons/hicolor/256x256/apps/pkforge.png" %{buildroot}/usr/share/icons/hicolor/256x256/apps/

%files
/usr/lib/pkforge/PKForge
/usr/bin/pkforge
/usr/share/applications/pkforge.desktop
/usr/share/icons/hicolor/256x256/apps/pkforge.png
EOF

    rpmbuild -bb --define "_topdir ${RPM_BUILD_DIR}" "${RPM_BUILD_DIR}/SPECS/pkforge.spec"
    GENERATED_RPM=$(find "${RPM_BUILD_DIR}/RPMS" -name '*.rpm' | head -1)
    if [[ -n "${GENERATED_RPM}" && -f "${GENERATED_RPM}" ]]; then
        mv "${GENERATED_RPM}" "${RPM_FILE}"
    fi
    rm -rf "${RPM_BUILD_DIR}"
else
    echo "Notice: rpmbuild not installed; skipping .rpm package creation on this host."
fi

# Clean up staging directories
rm -rf "${STAGE_DIR}" "${COMMON_ROOT}"

echo "Packaging complete! Artifacts created in ${OUTPUT_DIR}:"
ls -lh "${OUTPUT_DIR}"/pkforge*
