#!/usr/bin/env sh
# Count uploaded centrelines whose stored length is not the length of the passage they trace -- an
# earlier version measured every wall shot of a survey export, which reads several times too long.
# Prints numbers only and changes nothing; centerline-length-repair.sh rewrites the figures in place.
#
# Usage (from the deploy/ directory, with the stack running):
#   sh scripts/centerline-length-count.sh
set -eu
docker compose exec -T api dotnet SilexGis.Api.dll centerline-length-count
