#!/usr/bin/env bash
# The stable final CI check succeeds only when both required test jobs succeeded:
# the shared suites job (with the synthetic nests) and Engine.Tests.
set -euo pipefail

if [[ $# -ne 2 ]]; then
    printf 'Expected suites and engine-tests job results.\n' >&2
    exit 1
fi

if [[ "$1" != success || "$2" != success ]]; then
    printf 'Required CI jobs did not both succeed (suites=%s, engine=%s).\n' "$1" "$2" >&2
    exit 1
fi

printf 'All required unit suites, the synthetic nests and Engine.Tests passed.\n'
