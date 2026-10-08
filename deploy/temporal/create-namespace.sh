#!/bin/sh
# Registers the sandbox namespace, then exits.
#
# Runs in a one-shot admin-tools container after the server reports healthy. The
# healthcheck only proves the gRPC port is open, which happens before the frontend
# is ready to serve, so this retries rather than assuming.
set -eu

: "${TEMPORAL_ADDRESS:?TEMPORAL_ADDRESS is required}"

NAMESPACE="${TEMPORAL_NAMESPACE:-default}"
RETENTION="${TEMPORAL_RETENTION:-72h}"
MAX_ATTEMPTS="${MAX_ATTEMPTS:-30}"

attempt=1
while [ "$attempt" -le "$MAX_ATTEMPTS" ]; do
    if temporal operator namespace describe \
        --address "$TEMPORAL_ADDRESS" --namespace "$NAMESPACE" >/dev/null 2>&1; then
        echo "==> namespace '${NAMESPACE}' already registered"
        exit 0
    fi

    if temporal operator namespace create \
        --address "$TEMPORAL_ADDRESS" --namespace "$NAMESPACE" --retention "$RETENTION"; then
        echo "==> namespace '${NAMESPACE}' registered with ${RETENTION} retention"
        exit 0
    fi

    echo "    attempt ${attempt}/${MAX_ATTEMPTS} failed, retrying in 2s"
    attempt=$((attempt + 1))
    sleep 2
done

echo "namespace '${NAMESPACE}' could not be registered after ${MAX_ATTEMPTS} attempts" >&2
exit 1
