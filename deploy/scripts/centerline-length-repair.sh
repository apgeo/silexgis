#!/usr/bin/env sh
# Rewrite, in place, the stored length of exactly the uploaded centrelines centerline-length-count.sh
# calls longer or shorter than the passage they trace, measuring each one the way an upload now does.
# Only the length column of those rows is written; no row is added, removed or moved, and a second
# run finds nothing to do. Centrelines read out of a survey model, ones with no stored length and
# ones with no line geometry are left as they are. Without --apply it is a dry run: it makes the
# changes inside a transaction, counts again, prints what it did, and rolls everything back.
#
# Usage (from the deploy/ directory, with the stack running):
#   sh scripts/centerline-length-repair.sh            # dry run: prints what would change, changes nothing
#   sh scripts/centerline-length-repair.sh --apply    # commits, only if no length differs afterwards
#
# Take a backup first (sh scripts/backup.sh). If somebody uploads a centreline while this runs, the
# commit fails rather than overwriting their figure; run it again. Exits 1 when --apply could not
# commit, in which case nothing was changed.
set -eu
docker compose exec -T api dotnet SilexGis.Api.dll centerline-length-repair "$@"
