# SPDX-License-Identifier: AGPL-3.0-or-later
#
# The analysis service: WhiteboxTools (the open-core program, MIT-licensed) and the small web
# service the application hands a run to.
#
# Built from the server directory, so the service is compiled from the same sources as the
# application (docker-compose.whitebox.yml does this):
#   docker build -f deploy/whitebox/Dockerfile server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SilexGis.WhiteboxService -c Release -o /app/publish

# The program, built from the source of its tagged release, pinned by the checksum of that source
# archive, so a changed download fails the build instead of running. Built here rather than
# downloaded ready-made because the vendor publishes its ready-made programs at one unversioned
# address per platform: the day it publishes a newer release, a pinned download stops matching and
# can never be fetched again, and the image — with every update that rebuilds it — stops at the
# checksum. A tagged source archive does not change under its tag, and the packages it builds from
# are fixed by its own lock file and fetched from the Rust package registry, whose published
# versions are never replaced.
#
# The compiler is held at 1.79: the release's lock file pins a version of a date-and-time package
# that later compilers refuse. Only the command-line program is built and kept: the vendor's
# graphical runner and its extension programs, two of which download and install software, are
# taken out of the workspace before building. That is also what lets the build run at all: the
# lock file records, for a package only the extension programs use, a checksum the registry does
# not serve, so a build of the whole workspace stops on it. Without them the lock file can only
# lose entries, and the build checks that every package it fetched, by name, version and checksum,
# is one the release's lock file pins, so nothing is built that the release did not pin. The
# settings file is written here, because the vendor's names a working directory on its author's
# machine. The compiler builds for whichever processor the image is built for.
FROM rust:1.79-bookworm AS whitebox
ARG WHITEBOX_VERSION=2.4.0
ARG WHITEBOX_SOURCE_SHA256=f99f47e3d362cd3a1e8ac3f1e060ad7d0426244520ebd968f3ef01870fc1a955
RUN set -eu; \
    curl -fsSL -o /tmp/whitebox.tar.gz \
      "https://github.com/jblindsay/whitebox-tools/archive/refs/tags/v${WHITEBOX_VERSION}.tar.gz"; \
    echo "${WHITEBOX_SOURCE_SHA256}  /tmp/whitebox.tar.gz" | sha256sum -c -; \
    mkdir -p /build /out; \
    tar -xzf /tmp/whitebox.tar.gz -C /build --strip-components=1; \
    cd /build; \
    sed -i 's/^members = .*/members = ["whitebox-common", "whitebox-lidar", "whitebox-raster", "whitebox-tools-app", "whitebox-vector"]/' Cargo.toml; \
    cp Cargo.lock /tmp/Cargo.lock.pinned; \
    cargo build --release --package whitebox_tools --bin whitebox_tools; \
    pinned() { awk '/^name = /{n=$3} /^version = /{v=$3} /^checksum = /{print n, v, $3}' "$1" | sort; }; \
    pinned /tmp/Cargo.lock.pinned > /tmp/pinned.txt; \
    pinned Cargo.lock > /tmp/used.txt; \
    if [ -n "$(comm -13 /tmp/pinned.txt /tmp/used.txt)" ]; then \
      comm -13 /tmp/pinned.txt /tmp/used.txt >&2; echo "the build used packages the release did not pin" >&2; exit 1; \
    fi; \
    cp target/release/whitebox_tools LICENSE.txt /out/; \
    chmod 0755 /out/whitebox_tools; \
    printf '{\n  "verbose_mode": false,\n  "working_directory": "",\n  "compress_rasters": true,\n  "max_procs": -1\n}\n' > /out/settings.json; \
    /out/whitebox_tools --version | head -n 1 | grep -q "WhiteboxTools v${WHITEBOX_VERSION}"; \
    cd /; rm -rf /build /tmp/whitebox.tar.gz

# prlimit (util-linux) and timeout (coreutils), which bound each run's memory, file sizes and time,
# are part of the base image.
#
# bubblewrap is deliberately absent, for the reason the survey compiler's image gives: inside a
# container started with the default security profile it cannot make its namespaces, and the
# profile changes that would let it weaken the container's own boundary towards the host. Here the
# container is the sandbox: the overlay runs it on an internal network with no way out, with a
# read-only filesystem, no capabilities and limits on memory, processors and processes, and nothing
# in it is secret. So the service is told to run without bubblewrap, which the same runner then
# does with the same limits and an emptied environment.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=whitebox /out/ /opt/whitebox/
# Runs are made under /work, one directory per run, deleted when it ends. The overlay mounts a
# volume here; the directory must exist and belong to the service's user for a fresh volume to be
# seeded writable. The program's own directory stays root's and read-only.
RUN mkdir -p /work && chown "$APP_UID" /work \
    && command -v prlimit && command -v timeout
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=3200 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    SILEXGIS__WhiteboxService__WorkDir=/work \
    SILEXGIS__Analysis__Whitebox__WhiteboxPath=/opt/whitebox/whitebox_tools \
    SILEXGIS__Analysis__Whitebox__AllowUnsandboxed=true
EXPOSE 3200
ENTRYPOINT ["dotnet", "SilexGis.WhiteboxService.dll"]
