#!/usr/bin/env bash
# Prints everything the running stack exposes.
#
# Ports are read from deploy/.env rather than hardcoded, so this stays correct
# when someone changes one. Services that are not running are marked, which makes
# this double as a quick "what is actually up" check.
set -uo pipefail

cd "$(dirname "$0")/.."

[ -f deploy/.env ] || { echo "deploy/.env is missing. Run 'make up' first." >&2; exit 1; }

port() { sed -n "s/^$1=//p" deploy/.env | tr -d '\r' | head -1; }

running() {
    docker compose --env-file deploy/.env -f deploy/docker-compose.yml \
        ps --services --filter status=running 2>/dev/null
}

# Told apart from "never started", because for an opt-in service they mean opposite
# things. A load generator that died an hour into a soak and one that was never asked
# for both render as absent otherwise, and the first is the one worth knowing about.
exited() {
    docker compose --env-file deploy/.env -f deploy/docker-compose.yml \
        ps --services --filter status=exited 2>/dev/null
}

RUNNING=$(running)
EXITED=$(exited)

PG_USER=$(sed -n 's/^POSTGRES_USER=//p' deploy/.env | tr -d '\r' | head -1)
PG_PASSWORD=$(sed -n 's/^POSTGRES_PASSWORD=//p' deploy/.env | tr -d '\r' | head -1)

# service | label | url | note
rows=(
  "temporal-ui|Temporal UI|http://localhost:$(port PORT_TEMPORAL_UI)|workflows, history, search"
  "grafana|Grafana|http://localhost:$(port PORT_GRAFANA)|dashboards, no login"
  "prometheus|Prometheus|http://localhost:$(port PORT_PROMETHEUS)|query browser"
  "temporal|Server metrics|http://localhost:$(port PORT_TEMPORAL_METRICS)/metrics|all four roles"
  "worker|Worker metrics|http://localhost:$(port PORT_WORKER_METRICS)/metrics|.NET SDK"
  "adminer|Database UI|http://localhost:$(port PORT_ADMINER)/?pgsql=postgresql|log in as $PG_USER / $PG_PASSWORD"
  "loadgen|Load generator|http://localhost:$(port PORT_LOADGEN)/metrics|client view, 'make load'"
)

printf '\n  %s\n\n' "Open these:"
for row in "${rows[@]}"; do
    IFS='|' read -r service label url note <<<"$row"
    if printf '%s\n' "$RUNNING" | grep -qx "$service"; then
        mark=" "
    elif printf '%s\n' "$EXITED" | grep -qx "$service"; then
        mark="-"
        note="died, see 'make logs'"
    else
        mark="-"
        note="not running"
    fi
    printf '   %s %-16s %-42s %s\n' "$mark" "$label" "$url" "$note"
done

printf '\n  %s\n\n' "Straight to a dashboard:"
printf '     %-16s %s\n' "Server" "http://localhost:$(port PORT_GRAFANA)/d/temporal-sandbox-server"
printf '     %-16s %s\n' ".NET SDK" "http://localhost:$(port PORT_GRAFANA)/d/temporal-sdk-core"

printf '\n  %s\n\n' "Not browser endpoints:"
printf '     %-16s %s\n' "Temporal gRPC" "localhost:$(port PORT_TEMPORAL_GRPC)   (the temporal CLI default, so host commands just work)"
printf '     %-16s %s\n' "Postgres" "postgresql://${PG_USER}:${PG_PASSWORD}@localhost:$(port PORT_POSTGRES)/temporal   (a wire protocol, not a web page: use the Database UI above)"
echo
