# SPDX-License-Identifier: AGPL-3.0-or-later
#
# The survey compiler service: Therion, the TeX and MetaPost programs it draws with, the sandbox it
# starts them through, and the small web service the application hands a project to.
#
# Built from the server directory, so the service is compiled from the same sources as the
# application (docker-compose.therion.yml does this):
#   docker build -f deploy/therion/Dockerfile server
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SilexGis.TherionService -c Release -o /app/publish

# The compiler and what it runs, on the Ubuntu release whose Therion (6.3.4) is the one the
# application's log reader and its tests are written against; Debian's current release carries
# 6.2. The .NET runtime comes from the same archive. Therion draws maps by writing MetaPost and TeX
# and running them, so both come along; recommended packages do not, which keeps the editors and the
# desktop viewer out. prlimit (util-linux) and timeout (coreutils) bound each compile's memory,
# file sizes and time.
#
# bubblewrap is deliberately absent. Inside a container started with the default security profile
# it cannot make the namespaces its sandbox is built from, and the profile changes that would let it
# (no seccomp filter, no AppArmor profile, an unmasked /proc) weaken the container's own boundary
# towards the host by more than the inner sandbox adds. Here the container is the sandbox: the
# overlay runs it on an internal network with no way out, with a read-only filesystem, no
# capabilities, and limits on memory, processors and processes, and nothing in it is secret — no
# database password, no stored files, none of the application's settings. So the service is told to
# compile without bubblewrap, which the same runner then does with the same limits and an emptied
# environment.
FROM ubuntu:26.04 AS compiler
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        aspnetcore-runtime-10.0 therion texlive-base texlive-metapost util-linux coreutils \
    && rm -rf /var/lib/apt/lists/* \
    && therion --version \
    && useradd --system --no-create-home --shell /usr/sbin/nologin therion

FROM compiler AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# Runs are made under /work, one directory per run, deleted when it ends. The overlay mounts a
# volume here; the directory must exist and belong to the service's user for a fresh volume to be
# seeded writable.
RUN mkdir -p /work && chown therion:therion /work
USER therion
ENV ASPNETCORE_HTTP_PORTS=3100 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    SILEXGIS__SurveyCompiler__WorkDir=/work \
    SILEXGIS__SurveyCompiler__AllowUnsandboxed=true
EXPOSE 3100
ENTRYPOINT ["dotnet", "SilexGis.TherionService.dll"]
