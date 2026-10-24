# SPDX-License-Identifier: AGPL-3.0-or-later
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SilexGis.Api -c Release -o /app/publish

# The program that cuts a region out of a planet build when an administrator downloads the
# installation's own basemap: one static binary, pinned by version and by the checksum its release
# publishes, so a changed download fails the build instead of running.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS pmtiles
ARG TARGETARCH
ARG PMTILES_VERSION=1.31.2
RUN set -eu; \
    case "${TARGETARCH:-amd64}" in \
      amd64) asset="go-pmtiles_${PMTILES_VERSION}_Linux_x86_64.tar.gz"; \
             sum=3ed7dbf4ec2e6dfe5e25b6f70d1ffc932729f93c86db353bf514dd71010a312f ;; \
      arm64) asset="go-pmtiles_${PMTILES_VERSION}_Linux_arm64.tar.gz"; \
             sum=f8bd47e7ea866863489cad588fbaf2f31f42e5821f7a03f009b3769f05801cb1 ;; \
      *) echo "no pmtiles build for ${TARGETARCH}" >&2; exit 1 ;; \
    esac; \
    curl -fsSL -o /tmp/pmtiles.tar.gz "https://github.com/protomaps/go-pmtiles/releases/download/v${PMTILES_VERSION}/${asset}"; \
    echo "${sum}  /tmp/pmtiles.tar.gz" | sha256sum -c -; \
    mkdir -p /out; \
    tar -xzf /tmp/pmtiles.tar.gz -C /out pmtiles; \
    /out/pmtiles version

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# The PostgreSQL driver probes for Kerberos support while opening a connection. The library
# is absent from this base image, and the loader reports that miss on stderr as "Error:
# libgssapi_krb5.so.2: cannot open shared object file" — once per start, before any log line
# the application controls. Password authentication is unaffected, but the wording reads as a
# failed startup to anyone following the container logs, so carry the library and stay quiet.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
COPY --from=pmtiles /out/pmtiles /usr/local/bin/pmtiles
# Mount points for the uploads, data-protection-key and terrain volumes. Every one must exist
# in the image and be owned by the non-root app user: Docker seeds a fresh named volume from
# the image directory it covers, ownership included. A path missing here is created root-owned
# and the app cannot write it — for the key ring that breaks every cookie/token encryption, and
# for terrain it stops the first build before it has done anything.
#
# The terrain directories are named separately because the bake worker, when an installation runs
# one, shares this volume and writes into two of them as the same user this image declares. Seeding
# them here means a fresh volume comes up correct whichever container happens to mount it first —
# including the web front, which mounts the same volume read-only and serves finished pyramids out
# of the third. A serving rule aimed at a directory that is not there yet is not a failure, but it
# is one fewer thing to wonder about while looking at a 404.
#
# The basemap directory is the one the tile server reads (read-only) and this application writes a
# finished download into.
RUN mkdir -p /data/files /data/keys /data/terrain/builds /data/terrain/spool /data/terrain/published /data/basemap && chown -R app:app /data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SilexGis.Api.dll"]
