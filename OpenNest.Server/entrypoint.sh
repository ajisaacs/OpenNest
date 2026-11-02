#!/bin/sh
# OPENNEST_ROLE picks what this container runs: the nest server (default) or the nesting worker.
set -e
case "${OPENNEST_ROLE:-server}" in
  server) exec dotnet /app/OpenNest.Server.dll "$@" ;;
  worker) exec dotnet /app/worker/OpenNest.Server.Worker.dll "$@" ;;
  *) echo "OPENNEST_ROLE must be server or worker, not '$OPENNEST_ROLE'." >&2; exit 64 ;;
esac
