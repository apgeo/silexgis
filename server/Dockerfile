# SPDX-License-Identifier: AGPL-3.0-or-later
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SilexGis.Api -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Mount points for the uploads and data-protection-key volumes. Both must exist in the
# image and be owned by the non-root app user: Docker seeds a fresh named volume from the
# image directory it covers, ownership included. A path missing here is created root-owned
# and the app cannot write it — for the key ring that breaks every cookie/token encryption.
RUN mkdir -p /data/files /data/keys && chown -R app:app /data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SilexGis.Api.dll"]
