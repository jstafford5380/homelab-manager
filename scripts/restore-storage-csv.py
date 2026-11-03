"""Restore a storage CSV into a NEW data directory using a matching app database's schema.

The schema database is read-only and contributes no inventory, accounts or credentials.
Initialize one with the same app version if the original database is unavailable.
"""
import argparse
import base64
import csv
import hashlib
import json
import pathlib
import shutil
import sqlite3
import tempfile

HEADER = ["record_type", "record_id", "server_id", "drive_id", "name", "model", "serial", "alias", "pool", "capacity_bytes", "location", "notes", "record_json"]
INCLUDED = {
    "Connections", "Disks", "DiskAliases", "DiskAnnotations", "DriveAcquisitions", "Maintenance", "Replacements",
    "DrivePaths", "SpareReviews", "Assets", "DiscoveryAssignments", "IdentityMerges",
    "SmartReports", "SmartReads", "SelfTestEvidence", "Observations", "ApiPowerOnStates",
    "PoolSnapshots", "PoolScans", "PoolDailySummaries", "DatasetSamples", "DatasetRenames",
    "Capabilities", "NativeAlerts", "CollectionRuns", "CollectionDailySummaries", "Findings", "Plans", "PlanChanges"
}
csv.field_size_limit(64 * 1024 * 1024)


def quote(name):
    return '"' + name.replace('"', '""') + '"'


def rows(path):
    with path.open(encoding="utf-8-sig", newline="") as source:
        reader = csv.DictReader(source, strict=True)
        if reader.fieldnames != HEADER:
            raise ValueError("Not a supported storage CSV: unexpected header")
        for row in reader:
            if None in row or any(value is None for value in row.values()):
                raise ValueError("Malformed CSV row")
            yield row


def manifest_and_verify(path):
    manifest = None
    actual = {}
    for row in rows(path):
        if manifest is not None:
            raise ValueError("Manifest must be the final, unique record")
        table = row["record_type"]
        if table == "__manifest__":
            manifest = json.loads(row["record_json"])
            continue
        count, digest = actual.setdefault(table, [0, hashlib.sha256()])
        digest.update((row["record_json"] + "\n").encode("utf-8"))
        actual[table][0] = count + 1
    if not manifest or manifest.get("format") != "homelab-storage-csv" or manifest.get("version") != 2 or manifest.get("scope") != "storage":
        raise ValueError("Missing or unsupported storage CSV manifest")
    tables = manifest.get("tables")
    if not isinstance(tables, dict) or set(tables) != INCLUDED:
        raise ValueError("Invalid inventory table manifest")
    if set(actual) - set(tables):
        raise ValueError("CSV contains undeclared tables")
    for table, definition in tables.items():
        count, digest = actual.get(table, (0, hashlib.sha256()))
        if count != definition["Rows"] or digest.hexdigest().upper() != definition["Sha256"]:
            raise ValueError("Missing, modified or truncated records in " + table)
    return manifest


def restore(archive, schema_database, destination):
    if destination.exists():
        raise ValueError("Destination must not exist; existing inventory will never be overwritten")
    manifest = manifest_and_verify(archive)
    source = sqlite3.connect(schema_database.resolve().as_uri() + "?mode=ro", uri=True)
    try:
        migrations = [row[0] for row in source.execute('SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId')]
        if migrations != manifest["migrations"]:
            raise ValueError("Initialize a schema database with the same app/migration version as this export")
        schema = source.execute("SELECT type,name,sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY CASE type WHEN 'table' THEN 0 WHEN 'index' THEN 1 ELSE 2 END, name").fetchall()
        known = {name for kind, name, _ in schema if kind == "table" and not name.startswith("__EF")}
        if not INCLUDED <= known:
            raise ValueError("Inventory table coverage differs from the trusted app schema")
        for table, definition in manifest["tables"].items():
            columns = [{"Name": row[1], "Type": row[2], "PrimaryKey": row[5]} for row in source.execute("PRAGMA table_info(" + quote(table) + ")")]
            if columns != definition["Columns"]:
                raise ValueError("Column schema differs for " + table)
        destination.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="homelab-storage-restore-", dir=destination.parent) as temporary:
            staged = pathlib.Path(temporary) / "homelab.db"
            target = sqlite3.connect(staged)
            staged.chmod(0o600)
            try:
                for kind, _, ddl in schema:
                    if kind == "trigger":
                        continue
                    target.execute(ddl)  # DDL comes only from the operator's trusted local app schema, never the CSV.
                target.executemany('INSERT INTO __EFMigrationsHistory VALUES (?,?)', source.execute('SELECT MigrationId,ProductVersion FROM __EFMigrationsHistory'))
                # Insert in a single transaction with checks after all relationship parents exist.
                # RESTRICT is not deferred by SQLite; no updates/deletes are needed while importing.
                inserted = {}
                for row in rows(archive):
                    table = row["record_type"]
                    if table == "__manifest__":
                        continue
                    if table not in inserted:
                        inserted[table] = [0, hashlib.sha256()]
                    inserted[table][0] += 1
                    inserted[table][1].update((row["record_json"] + "\n").encode("utf-8"))
                    definition = manifest["tables"][table]
                    names = [column["Name"] for column in definition["Columns"]]
                    record = json.loads(row["record_json"])
                    if not isinstance(record, dict) or set(record) != set(names):
                        raise ValueError("Record fields do not match " + table)
                    values = []
                    for column in definition["Columns"]:
                        value = record[column["Name"]]
                        if isinstance(value, (dict, list)):
                            raise ValueError("Expected scalar database values")
                        if column["Name"].startswith("Protected") and value not in (None, ""):
                            raise ValueError("Storage exports must not include integration credentials")
                        if column["Type"].upper() == "BLOB" and value is not None:
                            value = base64.b64decode(value, validate=True)
                        values.append(value)
                    target.execute("INSERT INTO " + quote(table) + " (" + ",".join(map(quote, names)) + ") VALUES (" + ",".join("?" for _ in names) + ")", values)
                for table, definition in manifest["tables"].items():
                    count, digest = inserted.get(table, (0, hashlib.sha256()))
                    if count != definition["Rows"] or digest.hexdigest().upper() != definition["Sha256"]:
                        raise ValueError("CSV changed during restoration")
                for kind, _, ddl in schema:
                    if kind == "trigger":
                        target.execute(ddl)
                target.execute("UPDATE Connections SET ProtectedApiKey='',Enabled=0,NextAttemptUtc=NULL,Status='Restored inventory — re-enroll credentials before enabling'")
                if target.execute("PRAGMA foreign_key_check").fetchone() is not None:
                    raise ValueError("Inventory has broken relationships")
                if target.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                    raise ValueError("Restored database integrity check failed")
                target.commit()
            finally:
                target.close()
            destination.mkdir(mode=0o700)
            try:
                shutil.move(staged, destination / "homelab.db")
                (destination / "storage-restore-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
            except BaseException:
                shutil.rmtree(destination)
                raise
    finally:
        source.close()
    return manifest["exportedUtc"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=pathlib.Path)
    parser.add_argument("destination", type=pathlib.Path)
    parser.add_argument("--schema", required=True, type=pathlib.Path, help="Read-only app database with the export's migration version; only its schema is used")
    args = parser.parse_args()
    print("Restored inventory snapshot:", restore(args.archive, args.schema, args.destination))
    print("Use the NEW directory as Homelab__DataPath. Set up an owner account, review the inventory, and re-enroll NAS credentials before enabling collection.")
