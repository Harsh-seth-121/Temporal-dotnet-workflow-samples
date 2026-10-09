# Vendored files

## deploy/grafana/dashboards/temporal-core-sdks-otel.json

- Source: https://github.com/temporalio/dashboards
- Branch: `master` (not `main`)
- Path: `sdk/temporal-core-sdks-otel.json`
- Commit: `4994df2cf98a901ec78217c111eda95ff087480d`

The .NET SDK is built on sdk-core, so this is the matching dashboard. Its
histograms are in milliseconds, which is why the worker leaves the SDK's metric
naming flags at their defaults rather than switching to the Prometheus-conventional
`_total` and `_seconds` suffixes.

One change from upstream: `uid` was empty and is pinned to `temporal-sdk-core`, so
the dashboard keeps a stable URL across redeploys. Nothing else is modified.

The sibling `server/server-general.json` is deliberately **not** vendored. It is
built on the Angular graph panel, which Grafana 12 removed, and several of its
queries use labels the server does not emit (`temporal_service_type`,
`temporal_namespace`, `kubernetes_pod_name`). `sandbox-server.json` covers the
same ground with queries checked against the server's current metric names.
