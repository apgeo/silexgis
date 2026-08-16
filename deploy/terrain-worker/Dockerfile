# SPDX-License-Identifier: AGPL-3.0-or-later
#
# The terrain bake worker: the pre-baker, plus a loop that keeps it alive and waiting for work.
#
# The pre-baker is a one-shot command-line tool. Declared as a service exactly as it comes, it
# would start, find nothing on its command line to do, exit — and with a restart policy become a
# crash loop that says nothing useful. So the service the application talks to is this image: the
# same tool, with a supervisor in front of it that watches a directory for requests.
#
# The base is pinned to an exact release rather than a moving tag. What this tool writes — in
# particular whether its tiles are compressed — is a property of its version, and the rule those
# tiles have to be served under is derived from the bytes it produced. A tag that moved under us
# would change that rule silently, and a wrongly declared encoding draws no ground at all while
# every request answers 200.
FROM gaia3d/mago-3d-terrainer:1.14.2-release

# The jar carries its version in its file name, so the pin above and the path the supervisor runs
# have to move together. Resolved once, here, where getting it wrong fails the image build with a
# sentence naming what was found — rather than at the first bake, weeks later, as a worker that
# started perfectly and then could not find anything to run.
RUN set -eu; \
    jars="$(find /app -maxdepth 1 -name 'mago-3d-terrainer-*.jar')"; \
    count="$(printf '%s' "$jars" | grep -c . || true)"; \
    if [ "$count" != "1" ]; then \
        echo "expected exactly one mago-3d-terrainer jar in /app, found: [$jars]" >&2; \
        exit 1; \
    fi; \
    ln -s "$jars" /app/terrainer.jar

# The application and this worker write into the same volume, one after the other: the
# application prepares the rasters, this worker writes the tiles, and the application then reads
# the pyramid and stamps its manifest. So both must run as the same user, or each finds the
# other's files unwritable and every build stops on a permission error. 1654 is the id the
# application's own runtime image gives its unprivileged user; the two are one decision and
# changing either alone breaks the handover.
ARG APP_UID=1654
RUN groupadd --gid ${APP_UID} bake \
    && useradd --uid ${APP_UID} --gid ${APP_UID} --no-create-home --shell /usr/sbin/nologin bake

COPY bake-worker.sh /usr/local/bin/bake-worker
RUN chmod 0755 /usr/local/bin/bake-worker

# The mount points must exist in the image and belong to that user before privileges are dropped.
# Docker seeds a fresh named volume from the image directory it covers, ownership included, so a
# path missing here comes up owned by root and the first thing any build tries to write fails.
# That has happened on this project before, with a different directory.
RUN mkdir -p /data/terrain/spool /data/terrain/builds && chown -R ${APP_UID}:${APP_UID} /data
USER ${APP_UID}:${APP_UID}

ENV SILEXGIS_TERRAIN_ROOT=/data/terrain

ENTRYPOINT ["/usr/local/bin/bake-worker"]
