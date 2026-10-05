#!/usr/bin/env bash
# Generates the SQL that creates (or updates) the database schema of each version, from its EF Core
# migrations. Use it when the schema must be created WITHOUT starting the application: a DBA reviewing
# the SQL, a production deployment, a database you create by hand. For local development you do not need
# it: each API applies its migrations at startup. Guide: §1.6.
#
# The scripts are idempotent: they record applied migrations in "__EFMigrationsHistory" and skip them,
# so they can run against an empty database or one that is already partly up to date.
#
# Usage (from anywhere):
#   scripts/create-schemas.sh                 # every version that exists
#   scripts/create-schemas.sh 02-clean-hexagonal
# Output: artifacts/sql/<version>.sql (git-ignored). Apply one with, for example:
#   docker compose exec -T postgres psql -U shop -d shop_clean < artifacts/sql/02-clean-hexagonal.sql
set -euo pipefail

cd "$(dirname "$0")/.."

# One line per schema: output name | project that holds the migrations | DbContext (empty when the project has only one).
# Each new version adds its line here (04 has one line per module, 05 one per service).
targets=(
  "01-layered|01-layered/src/Shop.Layered.Data|"
  "02-clean-hexagonal|02-clean-hexagonal/src/Shop.Clean.Infrastructure|"
  "03-vertical-slice|03-vertical-slice/src/Shop.Slice.Api|"
)

only="${1:-}"
out="artifacts/sql"
mkdir -p "$out"
dotnet tool restore > /dev/null

for target in "${targets[@]}"; do
  IFS='|' read -r name project context <<< "$target"
  if [[ -n "$only" && "$name" != "$only"* ]]; then
    continue
  fi
  if [[ ! -d "$project" ]]; then
    echo "skip  $name ($project does not exist yet)"
    continue
  fi

  args=(ef migrations script --idempotent --project "$project" --output "$out/$name.sql")
  if [[ -n "$context" ]]; then
    args+=(--context "$context")
  fi
  dotnet "${args[@]}" > /dev/null
  echo "wrote $out/$name.sql"
done
