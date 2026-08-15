# SPDX-License-Identifier: AGPL-3.0-or-later
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SilexGis.Api -c Release -o /app/publish

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
# Mount points for the uploads, data-protection-key and terrain volumes. Every one must exist
# in the image and be owned by the non-root app user: Docker seeds a fresh named volume from
# the image directory it covers, ownership included. A path missing here is created root-owned
# and the app cannot write it — for the key ring that breaks every cookie/token encryption, and
# for terrain it stops the first build before it has done anything.
RUN mkdir -p /data/files /data/keys /data/terrain && chown -R app:app /data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SilexGis.Api.dll"]
