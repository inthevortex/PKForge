#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

VERSION="${1:-}"
if [[ -z "${VERSION}" ]]; then
    VERSION="$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' \
        "${ROOT_DIR}/src/PKForge.App/PKForge.App.csproj" | head -1)"
fi
VERSION="${VERSION#v}"
if [[ ! "${VERSION}" =~ ^[0-9]+(\.[0-9]+)*(-[A-Za-z][0-9A-Za-z.-]*)?$ ]]; then
    echo "Error: '${VERSION}' is not a version like 3.2.0 or 3.2.0-beta1." >&2
    exit 1
fi

# Each format spells a pre-release so it sorts before the final release:
# 3.2.0-beta1 becomes deb 3.2.0~beta1, rpm 3.2.0-0.beta1 and pacman 3.2.0beta1-1.
# Package files are named with their own format's version; the tarball keeps the tag's.
BASE_VERSION="${VERSION%%-*}"
PRERELEASE=""
if [[ "${VERSION}" == *-* ]]; then
    PRERELEASE="${VERSION#*-}"
    PRERELEASE="${PRERELEASE//-/.}"
fi
DEB_VERSION="${BASE_VERSION}${PRERELEASE:+~${PRERELEASE}}"
RPM_VERSION="${BASE_VERSION}"
RPM_RELEASE="1"
if [[ -n "${PRERELEASE}" ]]; then
    RPM_RELEASE="0.${PRERELEASE}"
fi
PACMAN_VERSION="${BASE_VERSION}${PRERELEASE}"

REPO="${2:-sofianeelhor/PKForge}"
MAINTAINER="sofianeelhor <43454096+sofianeelhor@users.noreply.github.com>"

ARCH="linux-x64"
OUTPUT_DIR="${ROOT_DIR}/artifacts"
STAGE_DIR="${OUTPUT_DIR}/stage-pkforge"
COMMON_ROOT="${OUTPUT_DIR}/stage-common"

# The SteamGridDB key is a local build-time secret and must never ship.
SGDB_KEY="${ROOT_DIR}/src/PKForge.App/Resources/Raw/sgdb.key"
if [[ -e "${SGDB_KEY}" ]]; then
    echo "Error: ${SGDB_KEY} exists; move it out of the source tree before packaging." >&2
    exit 1
fi

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

# Assets are embedded in the single-file bundle, so search file contents as well as names.
if [[ -n "$(find "${STAGE_DIR}" -name 'sgdb.key' -print -quit)" ]] \
    || grep -rqaF 'sgdb.key' "${STAGE_DIR}"; then
    echo "Error: sgdb.key ended up in the published payload; refusing to package." >&2
    exit 1
fi

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

# --- Runtime Dependencies ---
# The self-contained binary still loads system libraries: the .NET runtime needs ICU,
# OpenSSL and libstdc++, Skia links fontconfig, and Avalonia's X11 backend opens libX11,
# its extensions, the session-management libraries and GL/EGL at startup.
DEB_DEPENDS="libc6, libgcc-s1, libstdc++6, libfontconfig1"
DEB_DEPENDS+=", libicu78 | libicu77 | libicu76 | libicu75 | libicu74 | libicu73 | libicu72 | libicu71 | libicu70"
DEB_DEPENDS+=", libssl3t64 | libssl3"
DEB_DEPENDS+=", libx11-6, libice6, libsm6, libxrandr2, libxi6, libxcursor1, libxext6, libxfixes3, libegl1, libgl1"
PACMAN_DEPENDS=(glibc gcc-libs fontconfig icu openssl libx11 libice libsm libxrandr libxi libxcursor libxext libxfixes libglvnd)
# rpm distros name the ICU package differently (Fedora libicu, openSUSE libicu78), so match its soname.
RPM_ICU=""
for icu in $(seq 80 -1 60); do
    RPM_ICU+="${RPM_ICU:+ or }libicuuc.so.${icu}()(64bit)"
done
RPM_REQUIRES=(
    "libstdc++.so.6()(64bit)"
    "libgcc_s.so.1()(64bit)"
    "libfontconfig.so.1()(64bit)"
    "(${RPM_ICU})"
    "(libssl.so.3()(64bit) or libssl.so.1.1()(64bit))"
    "libX11.so.6()(64bit)"
    "libICE.so.6()(64bit)"
    "libSM.so.6()(64bit)"
    "libXrandr.so.2()(64bit)"
    "libXi.so.6()(64bit)"
    "libXcursor.so.1()(64bit)"
    "libXext.so.6()(64bit)"
    "libXfixes.so.3()(64bit)"
    "libEGL.so.1()(64bit)"
    "libGL.so.1()(64bit)"
)

# --- 2. Debian Package (.deb) ---
DEB_DIR="${OUTPUT_DIR}/stage-deb"
DEB_FILE="${OUTPUT_DIR}/pkforge_${DEB_VERSION}_amd64.deb"
echo "Creating ${DEB_FILE}..."
rm -rf "${DEB_DIR}"
mkdir -p "${DEB_DIR}/DEBIAN"
cp -a "${COMMON_ROOT}/usr" "${DEB_DIR}/"

INSTALLED_SIZE=$(du -sk "${DEB_DIR}/usr" | cut -f1)
cat <<EOF > "${DEB_DIR}/DEBIAN/control"
Package: pkforge
Version: ${DEB_VERSION}
Section: utils
Priority: optional
Architecture: amd64
Installed-Size: ${INSTALLED_SIZE}
Maintainer: ${MAINTAINER}
Homepage: https://github.com/${REPO}
Depends: ${DEB_DEPENDS}
Description: Pokémon save file editor
 Cross-platform Pokémon save editor built on PKHeX.Core.
EOF

if command -v dpkg-deb >/dev/null 2>&1; then
    dpkg-deb --build --root-owner-group "${DEB_DIR}" "${DEB_FILE}"
else
    (
        cd "${DEB_DIR}"
        echo "2.0" > debian-binary
        tar --owner=0 --group=0 --numeric-owner -czf control.tar.gz -C DEBIAN .
        tar --owner=0 --group=0 --numeric-owner -czf data.tar.gz -C . usr
        rm -f "${DEB_FILE}"
        ar -rc "${DEB_FILE}" debian-binary control.tar.gz data.tar.gz
    )
fi
rm -rf "${DEB_DIR}"

# --- 3. Arch Linux Pacman Package (.pkg.tar.zst) ---
PACMAN_DIR="${OUTPUT_DIR}/stage-pacman"
PACMAN_FILE="${OUTPUT_DIR}/pkforge-${PACMAN_VERSION}-1-x86_64.pkg.tar.zst"
echo "Creating ${PACMAN_FILE}..."
rm -rf "${PACMAN_DIR}"
mkdir -p "${PACMAN_DIR}"
cp -a "${COMMON_ROOT}/usr" "${PACMAN_DIR}/"

INSTALLED_BYTES=$(du -sb "${PACMAN_DIR}/usr" | cut -f1)
BUILD_DATE=$(date +%s)
cat <<EOF > "${PACMAN_DIR}/.PKGINFO"
pkgname = pkforge
pkgbase = pkforge
pkgver = ${PACMAN_VERSION}-1
pkgdesc = Pokémon save file editor
url = https://github.com/${REPO}
builddate = ${BUILD_DATE}
packager = ${MAINTAINER}
size = ${INSTALLED_BYTES}
arch = x86_64
license = GPL3
EOF
printf 'depend = %s\n' "${PACMAN_DEPENDS[@]}" >> "${PACMAN_DIR}/.PKGINFO"

# pacman keeps archive ownership on install, so every entry must be root-owned.
if command -v bsdtar >/dev/null 2>&1; then
    (
        cd "${PACMAN_DIR}"
        LANG=C bsdtar --uid 0 --gid 0 --uname root --gname root \
            -czf .MTREE --format=mtree \
            --options='!all,use-set,type,uid,gid,mode,time,size,md5,sha256,link' \
            .PKGINFO usr
        LANG=C bsdtar --uid 0 --gid 0 --uname root --gname root --no-fflags \
            -cf - .PKGINFO .MTREE usr | zstd -q -c -T0 -19 > "${PACMAN_FILE}"
    )
else
    echo "Notice: bsdtar not installed; creating the pacman package without .MTREE."
    tar --owner=0 --group=0 --numeric-owner --zstd -cf "${PACMAN_FILE}" -C "${PACMAN_DIR}" .PKGINFO usr
fi
rm -rf "${PACMAN_DIR}"

# --- 4. Red Hat / RPM Package (.rpm) ---
RPM_FILE="${OUTPUT_DIR}/pkforge-${RPM_VERSION}-${RPM_RELEASE}.x86_64.rpm"
if command -v rpmbuild >/dev/null 2>&1; then
    echo "Creating ${RPM_FILE}..."
    RPM_BUILD_DIR="${OUTPUT_DIR}/stage-rpm"
    rm -rf "${RPM_BUILD_DIR}"
    mkdir -p "${RPM_BUILD_DIR}"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

    cat <<EOF > "${RPM_BUILD_DIR}/SPECS/pkforge.spec"
%define _binary_payload w19.zstdio
# Stripping or splitting debug info would truncate the bundle appended to the single-file host.
%global debug_package %{nil}
%global __os_install_post %{nil}
%global _build_id_links none
Name:           pkforge
Version:        ${RPM_VERSION}
Release:        ${RPM_RELEASE}%{?dist}
Summary:        Pokémon save file editor
License:        GPL-3.0-or-later
URL:            https://github.com/${REPO}
Packager:       ${MAINTAINER}
AutoReqProv:    no
$(printf 'Requires:       %s\n' "${RPM_REQUIRES[@]}")

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
%dir /usr/lib/pkforge
/usr/lib/pkforge/*
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
