# Storage inventory CSV

Use **Export CSV** at the top of Storage. The authenticated download at `GET /api/v1/storage/export.csv` always includes every server and all saved storage inventory, including missing, retired and spare drives. Server selection, collapsed pools, pagination and collection feature flags do not limit the export. It reads the application database in one consistent transaction and makes no NAS requests.

The file includes NAS connection references and saved topology; drives and identities; acquisition/cost/warranty/location details; annotations, maintenance and replacements; drive-linked plans and their history; SMART reports/readings, observations and storage findings; dataset inventories/reviews; pool snapshots/scans/daily summaries; and storage collection history. It contains all **retained storage** records; already-pruned history cannot be recovered.

Only shared enclosure/controller/target asset records directly referenced by a saved drive-path or spare review accompany those storage records, so their relationships can be restored. Unrelated inventory assets and plans are omitted.

Hardware-page records, recovery policies, global settings/activity, accounts, notifications and application backup records are excluded. NAS integration credentials are blanked, including encrypted API keys. The file contains storage details, addresses, serial numbers and owner notes.

## Format version 2 (storage only)

This is one UTF-8 CSV, with a BOM, CRLF record separators and quoted fields. `record_type` identifies each table. `record_id` preserves the primary key, including composite keys. Readable columns show server/drive IDs, name, model, serial, alias, pool, capacity, location and notes where applicable.

`record_json` contains every persisted column and its exact scalar value for that record. This is the restoration source; the readable columns are for browsing. JSON preserves nulls separately from empty strings and zero, Unicode, quotes, line breaks, stable IDs, original timestamps, revisions and embedded snapshot data. Readable cells that resemble spreadsheet formulas are prefixed with an apostrophe; the original string remains intact inside `record_json`.

The final `__manifest__` row declares `scope: storage`, format/version, export time, applied migrations, the complete table/column/key list, and per-table record counts and SHA-256 checksums. Empty tables are declared too. Checksums detect accidental changes or truncation; they are not a digital signature. Keep the original downloaded file for restoration; spreadsheet editors can rewrite its contents.

## Restore into a separate installation

The included offline restore tool is tested against the exported records. It creates a **new data directory**, never overwrites an existing directory, and makes no NAS requests or changes.

1. Use a trusted database initialized by the **same app/migration version** as the export as the schema reference. An existing database can serve as the reference, or start and stop that app version with an empty data directory. The tool opens the reference read-only and copies only its schema and migration history, never its records or accounts.
2. Run:

   ```sh
   python3 scripts/restore-storage-csv.py inventory.csv /path/to/new-data --schema /path/to/reference/homelab.db
   ```

3. Start the app with `Homelab__DataPath` pointing at the new directory. Set up a new owner account. Review the restored inventory and re-enroll integration credentials before enabling collection.

The tool verifies schema/migration compatibility, all declared tables and columns, record counts, checksums, uniqueness, database integrity and foreign-key relationships. A failed restore does not publish a partial destination. Restored NAS connections are disabled. Other application sections start empty with their normal defaults. Existing source timestamps, connection revisions and historical evidence are preserved; restoration does not make them fresh.

This restores the **application's saved inventory and evidence**. It does not recreate ZFS pools, datasets or files on a NAS, and it is not a substitute for NAS configuration/data backups or the app's full database backup.
