#!/usr/bin/env bash
# The stable final CI check succeeds only when both required job groups succeeded.
set -euo pipefail

if [[ $# -ne 2 ]]; then
    printf 'Expected unit-test and synthetic-nest job results.\n' >&2
    exit 1
fi

if [[ "$1" != success || "$2" != success ]]; then
    printf 'Required CI jobs did not both succeed (unit=%s, synthetic=%s).\n' "$1" "$2" >&2
    exit 1
fi

printf 'All required unit suites and the synthetic-nest gate passed.\n'
