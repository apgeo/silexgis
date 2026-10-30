#!/usr/bin/env sh
# Rewrite the stored hierarchy state of exactly the features ancestry-count.sh names, from the
# containment edges, in one transaction. Without --apply it is a dry run: it makes the changes
# inside the transaction, counts again, prints what it did, and rolls everything back.
#
# Usage (from the deploy/ directory, with the stack running):
#   sh scripts/ancestry-repair.sh            # dry run: prints what would change, changes nothing
#   sh scripts/ancestry-repair.sh --apply    # commits, only if the count afterwards is zero
#
# Take a backup first (sh scripts/backup.sh). If somebody edits the hierarchy while this runs, the
# commit fails rather than overwriting their change; run it again. Exits 1 when --apply could not
# bring the count to zero, in which case nothing was changed.
set -eu
docker compose exec -T api dotnet SilexGis.Api.dll ancestry-repair "$@"
