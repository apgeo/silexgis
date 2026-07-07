# SPDX-License-Identifier: AGPL-3.0-or-later
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SilexGis.Api -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Uploaded files volume; owned by the non-root app user.
RUN mkdir -p /data/files && chown -R app:app /data
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SilexGis.Api.dll"]
