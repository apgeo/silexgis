#!/usr/bin/env sh
# Count features whose stored hierarchy state disagrees with their containment edges: the stored
# ancestor list, the closure rows and the effective-protection flag. Prints numbers only and
# changes nothing. Run it before and after ancestry-repair.sh.
#
# Usage (from the deploy/ directory, with the stack running):
#   sh scripts/ancestry-count.sh
#
# The figure that matters most is "shown as unprotected while a protected feature contains them":
# those rows show an exact position to accounts that may not see it until they are repaired.
set -eu
docker compose exec -T api dotnet SilexGis.Api.dll ancestry-count
