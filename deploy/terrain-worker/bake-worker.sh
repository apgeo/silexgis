#!/bin/sh
# SPDX-License-Identifier: AGPL-3.0-or-later
#
# Watches a spool directory for bake requests, runs the pre-baker on each one, and writes back
# what happened.
#
# The pre-baker is a command-line tool that runs once and exits, and the application that wants
# it run has no way to start a container — it is deliberately given no access to the container
# engine, which would be a far larger privilege than baking terrain is worth. So the two meet on
# a shared directory instead: the application writes what it wants baked, this loop bakes it, and
# writes back the outcome beside the request.
#
# One request is one directory under the spool:
#
#   <spool>/<name>/request     written by the application, then renamed into place
#   <spool>/<name>/started     written here, the moment this loop takes the request
#   <spool>/<name>/bake.log    everything the pre-baker said, as it says it
#   <spool>/<name>/result      written here, last, and renamed into place
#
# `request` is a list of `key=value` lines:
#
#   input=/data/terrain/builds/<id>/prepared   directory of rasters, one file per source
#   output=/data/terrain/builds/<id>/tiles     directory the pyramid is written into
#   maxDepth=13                                deepest tile level, always stated explicitly
#   datum=orthometric                          or `ellipsoidal`, to convert the heights while
#                                              baking
#
# `result` is the same shape:
#
#   status=succeeded|failed|refused|interrupted
#   exit=<the pre-baker's exit code, when it got as far as running>
#   reason=<one line, whenever the status is not succeeded>
#   finished=<UTC timestamp>
#
# Both files are written under a temporary name and renamed into place, and both sides must do
# it that way. A rename is the only way either side can be sure it is reading a whole file: a
# reader that happens to look while an ordinary write is half done sees a truncated one, and a
# truncated request is a bake of the wrong thing.
#
# Three things this loop deliberately does not do:
#
#   - It does not judge the bake. The pre-baker does not fail when it runs short of memory: it
#     stops refining and writes coarser tiles, saying so only in its own log. The whole log is
#     kept here, and whoever reads the result is the side that reads it and decides.
#   - It does not delete anything it did not create. The spool directory belongs to whoever wrote
#     the request, failures included — the log is wanted most exactly then.
#   - It does not retry. A request it has already started and not finished is reported as
#     interrupted rather than run again: the half-written output of an interrupted bake is not a
#     thing to start a second one on top of, and only the side that owns the build directory can
#     decide whether to clear it.
set -eu

ROOT="${SILEXGIS_TERRAIN_ROOT:-/data/terrain}"
SPOOL="${SILEXGIS_TERRAIN_SPOOL:-$ROOT/spool}"
POLL="${SILEXGIS_TERRAIN_POLL_SECONDS:-2}"
JAR=/app/terrainer.jar

now() {
    date -u '+%Y-%m-%dT%H:%M:%SZ'
}

say() {
    printf '%s %s\n' "$(now)" "$*"
}

# The value of one key in a request file, or nothing at all if it is absent. Matched as an exact
# prefix so that a value containing `=` survives, and with a trailing carriage return removed:
# the file may well have been written by a host that ends its lines that way, and a depth of
# "13\r" is not a number.
field() {
    awk -v key="$2" '
        index($0, key "=") == 1 {
            value = substr($0, length(key) + 2)
            sub(/\r$/, "", value)
            print value
            exit
        }
    ' "$1"
}

# Whether a path in a request is one this worker is allowed to touch: inside the terrain volume,
# and free of any parent-directory step that could climb back out of it. The request comes from
# the application rather than from a stranger, but a worker that will bake into whatever absolute
# path it is handed is a worse thing to have running than one that will not.
contained() {
    case "$1" in
        "$ROOT"/*) ;;
        *) return 1 ;;
    esac
    case "$1" in
        *..*) return 1 ;;
    esac
    return 0
}

write_result() {
    directory="$1"
    status="$2"
    code="$3"
    reason="$4"
    {
        printf 'status=%s\n' "$status"
        printf 'exit=%s\n' "$code"
        if [ -n "$reason" ]; then
            printf 'reason=%s\n' "$reason"
        fi
        printf 'finished=%s\n' "$(now)"
    } >"$directory/result.part"
    mv "$directory/result.part" "$directory/result"
}

refuse() {
    say "refused $1: $2"
    write_result "$1" refused "" "$2"
}

bake() {
    directory="$1"
    request="$directory/request"
    log="$directory/bake.log"

    input="$(field "$request" input)"
    output="$(field "$request" output)"
    depth="$(field "$request" maxDepth)"
    datum="$(field "$request" datum)"

    if ! contained "$input" || [ ! -d "$input" ]; then
        refuse "$directory" "input is not a directory inside $ROOT"
        return 0
    fi
    if ! contained "$output"; then
        refuse "$directory" "output is not a path inside $ROOT"
        return 0
    fi
    # Always stated, never inferred. Left to work it out for itself the pre-baker derives the
    # depth from the finest raster it was given, which for a small patch of half-metre survey
    # inside a coarse regional fill is several levels deeper than anyone wanted — and the price
    # of a bake tracks the number of tiles, not the area.
    case "$depth" in
        '' | *[!0-9]*)
            refuse "$directory" "maxDepth is not a number"
            return 0
            ;;
    esac
    if [ "$depth" -lt 1 ] || [ "$depth" -gt 22 ]; then
        refuse "$directory" "maxDepth $depth is outside 1-22"
        return 0
    fi

    # The input directory is deliberately not read-only anywhere in this chain: the pre-baker
    # refuses to start at all against an input it cannot write to, checked before it looks at
    # anything else. It does not in fact write there.
    set -- -i "$input" -o "$output" -max "$depth"
    case "$datum" in
        '' | orthometric) ;;
        ellipsoidal) set -- "$@" -g EGM2008 ;;
        *)
            refuse "$directory" "datum '$datum' is neither orthometric nor ellipsoidal"
            return 0
            ;;
    esac

    mkdir -p "$output"
    say "baking $input into $output, to depth $depth"

    # These mirror the flags the base image's own entrypoint carries, which this supervisor
    # replaces. The heap one is load-bearing: the tool ships no -Xmx and takes its heap as a
    # percentage of the container's memory limit, so that limit is the only knob there is, and
    # dropping this flag would quietly halve the heap. Short of heap it does not fail — it stops
    # refining and writes coarser tiles.
    code=0
    java \
        -Djava.awt.headless=true \
        -Dfile.encoding=UTF-8 \
        -XX:+UseContainerSupport \
        -XX:MaxGCPauseMillis=100 \
        -XX:+UseStringDeduplication \
        -XX:InitialRAMPercentage=50.0 \
        -XX:MaxRAMPercentage=50.0 \
        -jar "$JAR" "$@" >"$log" 2>&1 || code=$?

    if [ "$code" -eq 0 ]; then
        # The pre-baker leaves its own scratch directory inside the output, and it is as large as
        # the pyramid beside it. Removed here, while the run that made it is still the thing
        # happening: a machine with room for one pyramid does not also have room for the copy
        # nobody asked for.
        if [ -d "$output/temp" ]; then
            rm -rf "$output/temp"
        fi
        say "baked $output"
        write_result "$directory" succeeded "$code" ""
    else
        say "bake of $output failed with exit $code"
        write_result "$directory" failed "$code" "the pre-baker exited $code; its own account is in bake.log"
    fi
    return 0
}

case "$POLL" in
    '' | *[!0-9]*)
        say "SILEXGIS_TERRAIN_POLL_SECONDS must be a whole number of seconds, not '$POLL'"
        exit 1
        ;;
esac

if [ ! -f "$JAR" ]; then
    say "the pre-baker is missing from this image at $JAR"
    exit 1
fi

if ! mkdir -p "$SPOOL" 2>/dev/null || [ ! -w "$SPOOL" ]; then
    say "cannot write $SPOOL — the terrain volume must be mounted there and owned by uid $(id -u)"
    exit 1
fi

running=1
trap 'running=0' TERM INT

say "terrain bake worker watching $SPOOL every ${POLL}s"

while [ "$running" -eq 1 ]; do
    for request in "$SPOOL"/*/request; do
        if [ ! -f "$request" ]; then
            continue
        fi
        directory="$(dirname "$request")"
        if [ -e "$directory/result" ]; then
            continue
        fi
        if [ -e "$directory/started" ]; then
            say "$directory was started by an earlier run of this worker and never finished"
            write_result "$directory" interrupted "" \
                "the worker stopped while this bake was running; its output is unfinished"
            continue
        fi
        now >"$directory/started"
        bake "$directory"
    done
    sleep "$POLL"
done

say "terrain bake worker stopping"
