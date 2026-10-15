#!/usr/bin/env bash
# Regenerates the runtime inventories in the selected output directory from the reference image, in production
# mode with parity/.env.reference (plan M0 step 4):
#
#   PATH="$PWD/eng/docker-shim:$PATH" PARITY_SEED_DIR=... reference-tools/inventory/run.sh
#
#   rails-routes.txt        bin/rails routes
#   rails-middleware.txt    bin/rails middleware
#   effective-config.json   reference-tools/inventory/effective_config.rb
#   env-trace.json          environment variables read at boot and on a few requests (env_trace.rb)
#   routes-vector-check.txt reference-tools/campfire/routes.rb regenerated and diffed against
#                           vectors/campfire_routes.json (the vector itself is not rewritten)
#
# Every command runs in a throwaway instance on a copy of the default seed (parity/bin/reference
# exec --seed default), so the database is a real prepared one.
set -euo pipefail

ROOT=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
REFERENCE=$ROOT/parity/bin/reference
OUT=$ROOT/docs/inventory
SCRATCH=$(mktemp -d)
trap 'rm -rf "$SCRATCH"' EXIT
mkdir -p "$OUT"

echo "inventory: bin/rails routes" >&2
"$REFERENCE" exec --seed default -- bin/rails routes > "$OUT/rails-routes.txt"

echo "inventory: bin/rails middleware" >&2
"$REFERENCE" exec --seed default -- bin/rails middleware > "$OUT/rails-middleware.txt"

echo "inventory: effective configuration" >&2
"$REFERENCE" runner --seed default "$ROOT/reference-tools/inventory/effective_config.rb" > "$OUT/effective-config.json"

echo "inventory: environment trace" >&2
"$REFERENCE" exec --seed default \
  -e RUBYOPT=-r/work/reference-tools/inventory/env_trace.rb -e ENV_TRACE_OUT=/work/docs/inventory/env-trace.json -- \
  bin/rails runner /work/reference-tools/inventory/env_trace_workload.rb > /dev/null

echo "inventory: route vector check" >&2
"$REFERENCE" runner --seed default "$ROOT/reference-tools/campfire/routes.rb" > "$SCRATCH/campfire_routes.json"
{
  echo "# reference-tools/campfire/routes.rb in the current reference image vs vectors/campfire_routes.json"
  if cmp -s "$SCRATCH/campfire_routes.json" "$ROOT/vectors/campfire_routes.json"; then
    echo "identical ($(sha256sum < "$ROOT/vectors/campfire_routes.json" | cut -c1-64))"
  else
    diff -u "$ROOT/vectors/campfire_routes.json" "$SCRATCH/campfire_routes.json" || true
  fi
} > "$OUT/routes-vector-check.txt"
cat "$OUT/routes-vector-check.txt" >&2
