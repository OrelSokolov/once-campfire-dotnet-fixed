# syntax=docker/dockerfile:1
#
# The media test toolchain: Debian trixie with the exact libvips and ffmpeg builds the reference
# image ships, and the pinned .NET SDK, so the storage tests can compare variants and video previews
# byte for byte with vectors/storage/. It is a test image, not the application image.
#
#   eng/toolchain/test                  # builds this image with Podman and runs the storage tests
#
# Pins, the same as the reference image (parity/docker/reference.Dockerfile) and the .NET image
# (/Dockerfile); see docs/development.md:
#   * debian:trixie-slim by index digest, and every apt package from snapshot.debian.org at
#     20260918T000000Z, so libvips, ffmpeg and everything they link (libjpeg, libwebp, libspng,
#     libheif, ...) are the reference's builds: libvips42t64 8.16.1-1+deb13u1 and
#     ffmpeg 7:7.1.5-0+deb13u1, also the versions Rust pins at 3fe9f61.
#   * The .NET SDK 11.0.100-rc.2.26504.105 from Microsoft's tarball, checked against the SHA-512 in
#     the 11.0 release metadata.
# eng/toolchain/test prints the versions it ran with.

ARG DEBIAN_IMAGE=docker.io/library/debian:trixie-slim@sha256:a99cfc517144bc59b1978475ec53b46ecabec7e43635402ee5b77cc54cd1b20a

FROM ${DEBIAN_IMAGE}

ARG LIBVIPS_VERSION=8.16.1-1+deb13u1
ARG FFMPEG_VERSION=7:7.1.5-0+deb13u1
ARG DEBIAN_SNAPSHOT=20260918T000000Z

# Point apt at the snapshot before the first update (its Release files are past their Valid-Until
# dates, which is what snapshots are for), and fail if any source still names the live mirror.
RUN sed -i \
      -e "s|^URIs: http://deb.debian.org/debian-security\$|URIs: http://snapshot.debian.org/archive/debian-security/${DEBIAN_SNAPSHOT}|" \
      -e "s|^URIs: http://deb.debian.org/debian\$|URIs: http://snapshot.debian.org/archive/debian/${DEBIAN_SNAPSHOT}|" \
      /etc/apt/sources.list.d/debian.sources && \
    ! grep -q deb.debian.org /etc/apt/sources.list.d/debian.sources && \
    printf '%s\n' 'Acquire::Check-Valid-Until "false";' 'Acquire::Retries "5";' > /etc/apt/apt.conf.d/99snapshot

# The media libraries under test, plus what the .NET SDK needs at run time.
RUN apt-get update -qq && \
    apt-get install --no-install-recommends -y \
      libvips42t64=${LIBVIPS_VERSION} ffmpeg=${FFMPEG_VERSION} \
      ca-certificates curl libicu76 libssl3t64 zlib1g tzdata && \
    rm -rf /var/lib/apt/lists/* /var/cache/apt/archives

# As in the application image (/Dockerfile): libvips loads its modules into the global symbol scope,
# and the OpenSlide module links the system libsqlite3, which the SQLite bundled with
# Microsoft.Data.Sqlite (e_sqlite3) then binds its unresolved internal calls to. The storage tests
# open databases while another test's vips_init loads the modules, and the test host crashes
# (SIGSEGV in sqlite3_open_v2). Rails blocks the OpenSlide loader anyway, so the module goes.
RUN rm -f /usr/lib/*/vips-modules-*/vips-openslide.so && \
    ! ldd /usr/lib/*/vips-modules-*/*.so | grep -q libsqlite3

# The pinned SDK from Microsoft's tarball, checked against the SHA-512 in
# https://builds.dotnet.microsoft.com/dotnet/release-metadata/11.0/releases.json.
ARG DOTNET_SDK_VERSION=11.0.100-rc.2.26504.105
ARG DOTNET_SDK_SHA512_AMD64=54e9463339337cef68f455e97dadac0a9e1b49416a3b15504b577144b24ae4b4eed4e7ba3bac507650b61edf704e977b4627d3804f4c2688cc5bd69b4f2eecdd
ARG DOTNET_SDK_SHA512_ARM64=2ebaea263b9428af20f97fd8b5379e1248f21401e8345b5290387ff5deeda84664394da2f9ec480453056c307ed8a4e86bf675219e904a6e1d06b156525aa958
ARG TARGETARCH=amd64

RUN case "${TARGETARCH}" in \
      amd64) rid=linux-x64; sha512="${DOTNET_SDK_SHA512_AMD64}" ;; \
      arm64) rid=linux-arm64; sha512="${DOTNET_SDK_SHA512_ARM64}" ;; \
      *) echo "unsupported architecture ${TARGETARCH}" >&2; exit 1 ;; \
    esac && \
    curl -fsSL -o /tmp/dotnet-sdk.tar.gz \
      "https://builds.dotnet.microsoft.com/dotnet/Sdk/${DOTNET_SDK_VERSION}/dotnet-sdk-${DOTNET_SDK_VERSION}-${rid}.tar.gz" && \
    echo "${sha512}  /tmp/dotnet-sdk.tar.gz" | sha512sum -c - && \
    mkdir -p /usr/share/dotnet && \
    tar -xzf /tmp/dotnet-sdk.tar.gz -C /usr/share/dotnet && \
    rm /tmp/dotnet-sdk.tar.gz && \
    ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_PACKAGES=/nuget/packages

# Record what the tests will run against.
RUN dotnet --version && \
    { echo "debian snapshot: ${DEBIAN_SNAPSHOT}"; \
      echo "dotnet sdk: $(dotnet --version)"; \
      dpkg-query -W libvips42t64 ffmpeg libjpeg62-turbo libwebp7 libspng0 libpng16-16t64 libheif1 libexif12 liblcms2-2 libhwy1t64 zlib1g; \
    } > /etc/campfire-media-toolchain

WORKDIR /work
