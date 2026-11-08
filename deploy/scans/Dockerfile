# SPDX-License-Identifier: AGPL-3.0-or-later
#
# The point-cloud worker image, in two targets:
#
#   tiler      (the overlay's scans-worker service) — Python with py3dtiles, laspy and numpy, and
#              the worker loop. Tiles a LAZ/LAS into 3D Tiles, estimates and makes downsampled
#              display copies. Small, and enough for every installation that uploads finished
#              clouds. PDAL is not used: it is not packaged for Ubuntu 24.04, and laspy + numpy do
#              the downsampling.
#   hdmapping  the same, plus HDMapping's two headless programs (LiDAR-inertial odometry and
#              session export) built from a pinned commit, for installations that process raw
#              scanner recordings on the server. Its build compiles HDMapping and takes most of an
#              hour; `docker build --target tiler` never touches it. The commit is pinned because
#              HDMapping's command line is young and changes often, and it is recorded on every run.
#              The overlay's scans-hdmapping service (behind the "hdmapping" profile) runs it as a
#              second worker that takes only the HDMapping steps and the export check; the tiler
#              keeps tiling.
#
# Both run as uid 1654, the application's own runtime user, because both write the volume the
# application hands jobs over on.
ARG HDMAPPING_COMMIT=5bb54e071686228687a5bc76fdeb10b2e898808a
ARG CMAKE_VERSION=4.0.0

# ---- HDMapping, compiled ---------------------------------------------------------------------
FROM ubuntu:24.04 AS build
ARG HDMAPPING_COMMIT
ARG CMAKE_VERSION
ARG BUILD_JOBS=8
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      ca-certificates git wget g++ make libeigen3-dev libglew-dev freeglut3-dev libx11-dev libxi-dev libxrandr-dev \
      libxinerama-dev libxcursor-dev libxext-dev libtbb-dev liblaszip-dev libopencv-dev libproj-dev liblz4-dev libzstd-dev \
    && rm -rf /var/lib/apt/lists/*
RUN wget -q -O /tmp/cmake.sh https://github.com/Kitware/CMake/releases/download/v${CMAKE_VERSION}/cmake-${CMAKE_VERSION}-linux-x86_64.sh \
    && sh /tmp/cmake.sh --skip-license --prefix=/usr/local && rm /tmp/cmake.sh
WORKDIR /src
# The bundled OpenCV, oneTBB, freeglut and LASzip are not used (system packages are), so their
# submodules are not fetched: OpenCV alone is several hundred megabytes of history. Eigen is always
# taken from its submodule, whatever the build options say.
RUN git init -q hdmapping && cd hdmapping \
    && git remote add origin https://github.com/MapsHD/HDMapping.git \
    && git fetch -q --depth 1 origin ${HDMAPPING_COMMIT} && git checkout -q FETCH_HEAD \
    && for m in $(git config -f .gitmodules --get-regexp 'submodule\..*\.path' | awk '{print $2}'); do \
         case "$m" in 3rdparty/opencv|3rdparty/oneTBB|3rdparty/freeglut|3rdparty/LASzip) ;; \
         *) git submodule update -q --init --depth 1 "$m" ;; esac; done
RUN cmake -S hdmapping -B build -DCMAKE_BUILD_TYPE=Release \
      -DBUILD_WITH_BUNDLED_FREEGLUT=0 -DBUILD_WITH_BUNDLED_EIGEN=0 -DBUILD_WITH_BUNDLED_LIBLASZIP=0 \
      -DBUILD_WITH_BUNDLED_ONETBB=0 -DBUILD_WITH_CLI_TOOLS=ON \
    && cmake --build build -j${BUILD_JOBS} --target lidar_odometry_step_1 multi_view_tls_registration_step_2 \
    && mkdir -p /out && find build -type f -executable \( -name lidar_odometry_step_1 -o -name multi_view_tls_registration_step_2 \) -exec cp {} /out/ \;

# ---- The tiler: the worker loop and the Python tools ---------------------------------------------
FROM ubuntu:24.04 AS tiler
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      ca-certificates python3 python3-venv \
    && rm -rf /var/lib/apt/lists/*
# Every package pinned. py3dtiles below 12 cannot read LAZ and has no --spec-version or
# --extra-fields; laspy's LAZ backend (lazrs) is what makes a .laz readable at all. What this
# image writes is a property of these exact versions, and the application records them per run.
RUN python3 -m venv /opt/py && /opt/py/bin/pip install --no-cache-dir \
      "py3dtiles[las]==12.1.1" "laspy[lazrs]==2.7.0" "lazrs==0.8.2" "numpy==2.3.5"

# The application and this worker write into the same volume, one after the other: the
# application copies a cloud into a job directory, this worker writes the tiles beside it, and the
# application reads them back. So both must run as the same user, or each finds the other's files
# unwritable. 1654 is the id the application's own runtime image gives its unprivileged user; the
# two are one decision and changing either alone breaks the handover.
ARG APP_UID=1654
RUN groupadd --gid ${APP_UID} worker \
    && useradd --uid ${APP_UID} --gid ${APP_UID} --no-create-home --shell /usr/sbin/nologin worker

COPY scan-worker.py /usr/local/bin/scan-worker
RUN chmod 0755 /usr/local/bin/scan-worker

# The mount point must exist in the image and belong to that user before privileges are dropped.
# Docker seeds a fresh named volume from the image directory it covers, ownership included, so a
# path missing here comes up owned by root and the first job fails on its first write.
RUN mkdir -p /data/scans/jobs && chown -R ${APP_UID}:${APP_UID} /data/scans
USER ${APP_UID}:${APP_UID}

ARG WORKER_VERSION=1
ENV SILEXGIS_SCANS_ROOT=/data/scans \
    SILEXGIS_SCANS_WORKER_VERSION=${WORKER_VERSION} \
    PATH=/opt/py/bin:$PATH \
    PYTHONUNBUFFERED=1 \
    NUMBA_CACHE_DIR=/tmp/numba-cache
WORKDIR /data/scans
# The tiler compiles a few of its functions at start (numba) and caches them to disk; with a
# read-only filesystem and no home directory the only place for that cache is the /tmp the overlay
# mounts, and without a place it refuses to start at all.
# -I: the worker ignores PYTHONPATH and the current directory, so nothing dropped into a job
# directory can be imported by it.
ENTRYPOINT ["/opt/py/bin/python3", "-I", "/usr/local/bin/scan-worker"]

# ---- The tiler plus HDMapping -----------------------------------------------------------------
FROM tiler AS hdmapping
ARG HDMAPPING_COMMIT
USER root
# Both HDMapping programs run headless; no display and no xvfb are needed (measured on real
# recordings).
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
      libglew2.2 libglut3.12 libx11-6 libxi6 libxrandr2 libxinerama1 libxcursor1 libxext6 libtbb12 liblaszip8 \
      libopencv-core406t64 libopencv-imgproc406t64 libopencv-imgcodecs406t64 libopencv-calib3d406t64 \
      libproj25 liblz4-1 libzstd1 libgl1 libglu1-mesa \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/ /usr/local/bin/
# The same loop, taking the HDMapping steps only: it shares the hand-off directory with the tiler,
# and each answers its own steps' jobs and announces its own entries.
ENV HDMAPPING_COMMIT=${HDMAPPING_COMMIT} \
    SILEXGIS_SCANS_WORKER_ROLE=hdmapping
USER 1654:1654
