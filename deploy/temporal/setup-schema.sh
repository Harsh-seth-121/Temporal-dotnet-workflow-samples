#!/bin/sh
# Creates and migrates the two databases Temporal needs, then exits.
#
# Runs in a one-shot admin-tools container before the server starts.
#
# Every command here is already idempotent, verified against admin-tools 1.32.0:
#   create                -> exits 0 when the database exists
#   setup-schema -v 0.0   -> exits 0, logs "Skip version upgrade"
#   update-schema         -> exits 0, logs "found zero updates from current version"
# So none of them are guarded. A non-zero exit means something genuinely went
# wrong (bad credentials, unreachable host) and must stop the stack rather than
# surface later as a confusing error from a different command.
set -eu

: "${POSTGRES_SEEDS:?POSTGRES_SEEDS is required}"
: "${POSTGRES_USER:?POSTGRES_USER is required}"
: "${SQL_PASSWORD:?SQL_PASSWORD is required}"

DB_PORT="${DB_PORT:-5432}"
SCHEMA_ROOT=/etc/temporal/schema/postgresql/v12

# temporal-sql-tool's --plugin defaults to mysql8, so postgres12 is passed on
# every invocation. The password comes from SQL_PASSWORD, not POSTGRES_PWD.
sql_tool() {
    temporal-sql-tool \
        --plugin postgres12 \
        --ep "$POSTGRES_SEEDS" \
        -u "$POSTGRES_USER" \
        -p "$DB_PORT" \
        "$@"
}

setup_database() {
    database="$1"
    schema_dir="$2"

    echo "==> ${database}: create database"
    sql_tool --db "$database" create

    echo "==> ${database}: initialize schema"
    sql_tool --db "$database" setup-schema -v 0.0

    echo "==> ${database}: migrate to latest"
    sql_tool --db "$database" update-schema -d "$schema_dir"
}

setup_database temporal "${SCHEMA_ROOT}/temporal/versioned"
setup_database temporal_visibility "${SCHEMA_ROOT}/visibility/versioned"

echo "==> schema ready"
