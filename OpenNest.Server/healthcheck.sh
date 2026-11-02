#!/bin/sh
# Server: /healthz answers. Worker: its alive file was touched within the last two minutes.
if [ "${OPENNEST_ROLE:-server}" = worker ]; then
  alive="${OPENNEST_WORKER_ALIVE_FILE:-/tmp/opennest-worker.alive}"
  [ -n "$(find "$alive" -mmin -2 2>/dev/null)" ]
else
  curl --fail --silent --show-error http://127.0.0.1:8090/healthz > /dev/null
fi
