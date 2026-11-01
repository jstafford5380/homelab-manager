# NAS read manifest

Owner-reported installed target: **TrueNAS 24.10.2.4**. The application release guard accepts this patch family. The official `TS-24.10.2.4` tag resolves to `dee0b599f18b9d158bd0eef9ca8276b954bf941f`. Ten selected files covering the fixed read implementations, dataset projection, authentication, WebSocket dispatcher and disk event registration are byte-for-byte identical to the reviewed 24.10.2 baseline. [The source-comparison record](truenas-24.10.2.4-source-comparison.json) includes immutable source links and SHA-256 hashes. This is a bounded source comparison, not a complete dependency audit or live API/permission validation.

This application is monitor-and-advise only. `TrueNasClient.ReadMethods` is a frozen exact-match allowlist, checked at the public reader and immediately before WebSocket transmission. Read arguments are fixed in code and verified again at the RPC boundary. There is no configurable method, shell command, arbitrary parameter, test, scrub, repair or drive-control surface.

| Method | Fixed arguments | Purpose |
| --- | --- | --- |
| `system.info` | `[]` | Release guard and supplementary system serial identity |
| `disk.query` | `[]` | Inventory reconciliation; configured inventory interval |
| `pool.query` | `[]` | Pool state, curated topology and capacity/scan snapshots |
| `alert.list` | `[]` | Native alert evidence; no acknowledgment sent to the NAS |
| `pool.dataset.query` | Fixed bounded projection below | Optional dataset usage; off by default |
| `disk.temperatures` | `[[], {"only_cached": true}]` | Optional existing temperature cache; off by default |

`auth.login_with_api_key` is the sole extra RPC, used to establish a session. TLS validates the logical endpoint hostname even through an externally managed tunnel. Version/identity guards run before the storage reads. API keys are not logged or returned by application APIs.

## Cache-only temperature contract

The reviewed reference is TrueNAS middleware commit `ab87b14d2e721ec6fe6f917be02090c7e98a52df` (release/24.10.2), [disk_/temperature.py](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/disk_/temperature.py). The `disk.temperatures` method declares `REPORTING_READ`. With `only_cached=true`, it returns the existing in-memory cache after filtering entries to an age below 610 seconds. An empty names list first discovers eligible names through `disk.query`; it does not mean that the application may change the options. The reviewed [disk.query implementation](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/disk.py) defaults `supports_smart`, `passwords` and `pools` extras to false; this app cannot supply those extras. In particular, enabling `supports_smart` could issue a fresh smartctl query and is not permitted. The cache branch does not invoke `disk.temperature` or `disk.smartctl` and does not refresh the cache. Only that branch is authorized by the fixed arguments above.

The wire-level TLS/WebSocket test verifies those arguments, and a private-boundary regression test rejects missing arguments, `only_cached=false`, extra power-mode options and caller-selected device lists. Read-method tests reject `disk.smart_attributes`, `disk.smartctl`, `disk.temperature` and SMART test methods before network use.

This distinction matters: the reference [disk_/smart_attributes.py](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/disk_/smart_attributes.py) invokes `disk.smartctl` with `-A -j`; it is not a cache-only read and does not expose a no-wake option. It remains excluded. The generic/private `disk.smartctl` surface accepts command arguments and also remains excluded. Do not broaden the manifest to obtain metrics without reviewing side effects and argument restrictions.

Cache data is a map from reported device name to integer Celsius temperature or null. The app accepts at most 256 unique bounded device names and retains only the latest normalized snapshot. Invalid/missing readings stay unknown. Values outside the supported integer range of -273 through 1000 Celsius are not interpreted. An empty cache is not zero-filled. Fetch time is recorded separately; source observation times and durable physical-drive identity are absent. Consequently these readings are not inserted into physical-drive observations, used to resolve findings, or used for replacement advice. The cache is not proof of current temperatures or full SMART coverage.

## Dataset usage contract

Reviewed against the same pinned [24.10.2 dataset query](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/pool_/dataset.py) and [property projection](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/pool_/utils.py). Fixed arguments:

```json
[[], {"limit":257,"select":["id","name","pool","type","used","available","usedbysnapshots","quota","refquota"],"extra":{"flat":true,"retrieve_children":false,"properties":["used","available","usedbysnapshots","quota","refquota"]}}]
```

This metadata query does not request file traversal, recursive children, snapshots, arbitrary user properties, or encryption credentials. The API filters internal datasets. Its result limit is applied after source enumeration; it bounds the response, not server enumeration work. 257 rows signal an inventory above the app's 256-entry limit and reject the snapshot, retaining prior evidence. The transport also caps incoming frames at 4 MiB. No partial list is presented as complete.

Curated parsed byte values are nonnegative safe integers or unknown. Zero quota means no quota set; missing quota is unknown. Dataset names are not stable GUIDs: the reviewed projection does not expose dataset GUID, so this adapter does not infer historical continuity or renames. Parent/descendant usage overlaps, available space can reflect shared pool/quota limits, and dataset sums are not physical pool capacity. The latest snapshot is saved for current display. DatasetInventoryHistory additionally retains the first successful inventory per UTC hour and connection revision, including source serial/version context, under the configured detailed-history retention. These are sampled inventories, not continuous per-dataset identities or rename evidence. No additional NAS reads or properties are requested. There are no dataset mutations, inferred health findings or recovery claims. Boundary and TLS wire tests cover the fixed read and prohibit caller-selected parameters.

## Release gates

Source inspection and synthetic contract fixtures are not a live compatibility claim. Before enabling either optional dataset or cache read, verify the installed 24.10 patch's method/argument semantics and least-privilege permissions, collect sanitized real payloads, and verify that missing/disabled/denied/stale behavior matches the UI. Do not grant a broader credential just to turn an unavailable capability green. Live traffic, NAS load, drive power-state behavior, and the seven-day reference-host pilot remain unverified. No API call in this manifest starts a SMART test or NAS maintenance task.
