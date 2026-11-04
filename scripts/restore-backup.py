"""Restore an application backup into a NEW data directory with collectors disabled."""
import argparse
import hashlib
import json
import pathlib
import shutil
import sqlite3
import tempfile
import zipfile


def restore(archive, destination):
    if destination.exists():
        raise ValueError("Destination must not exist; existing state will never be overwritten")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="homelab-restore-", dir=destination.parent) as temporary:
        staging = pathlib.Path(temporary)
        with zipfile.ZipFile(archive) as bundle:
            names = bundle.namelist()
            if sorted(names) != ["homelab.db", "manifest.json"]:
                raise ValueError("Unexpected or duplicate archive entries")
            if bundle.getinfo("manifest.json").file_size > 1024 * 1024 or bundle.getinfo("homelab.db").file_size > 10 * 1024**3:
                raise ValueError("Archive exceeds restore limits")
            manifest = json.loads(bundle.read("manifest.json"))
            if manifest.get("formatVersion") != 1:
                raise ValueError("Unsupported backup version")
            database = staging / "homelab.db"
            with bundle.open("homelab.db") as source, database.open("wb") as target:
                shutil.copyfileobj(source, target)
        with database.open("rb") as source:
            digest = hashlib.file_digest(source, "sha256").hexdigest()
        if digest.upper() != manifest["databaseSha256"].upper():
            raise ValueError("Database checksum mismatch")
        with sqlite3.connect(database) as db:
            if db.execute("PRAGMA integrity_check").fetchone()[0] != "ok":
                raise ValueError("Database integrity check failed")
            actual = [row[0] for row in db.execute('SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId')]
            if actual != sorted(manifest["migrations"]):
                raise ValueError("Migration history differs from the manifest")
            # New host has a new key ring. Require explicit credential re-enrollment before collection.
            db.execute("UPDATE Connections SET ProtectedApiKey='', Enabled=0, Status='Restored — re-enroll credentials before enabling'")
            db.execute("UPDATE Settings SET BackupsEnabled=0")
            if db.execute("SELECT COUNT(*) FROM sqlite_master WHERE name='HardwareServers'").fetchone()[0]:
                db.execute("UPDATE HardwareServers SET ProtectedCommunity=NULL, Connected=0, NextAttemptUtc=NULL WHERE Platform='synology'")
            if db.execute("SELECT COUNT(*) FROM sqlite_master WHERE name='NotificationChannels'").fetchone()[0]:
                db.execute("UPDATE NotificationChannels SET Enabled=0, TestedUtc=NULL, ProtectedUrl=''")
                db.execute("UPDATE Deliveries SET Status='cancelled-after-restore' WHERE Status='pending'")
            if db.execute("SELECT COUNT(*) FROM sqlite_master WHERE name='ApiKeys'").fetchone()[0]:
                # Restoring an old archive must never reactivate a revoked credential.
                db.execute("UPDATE ApiKeys SET RevokedUtc=COALESCE(RevokedUtc, datetime('now'))")
            db.execute("INSERT INTO Activity (AtUtc, Actor, Action, Detail) VALUES (datetime('now'), 'operator', 'Backup restored', 'Collectors disabled; integration credentials require re-enrollment')")
        # The archived database can retain WAL mode. Checkpoint committed safety changes
        # before moving the main file; a context manager commits but does not close sqlite3.
        db.execute("PRAGMA wal_checkpoint(TRUNCATE)")
        db.close()
        database.chmod(0o600)
        destination.mkdir(mode=0o700)
        try:
            shutil.move(database, destination / "homelab.db")
            (destination / "restore-manifest.json").write_text(json.dumps(manifest, indent=2))
        except BaseException:
            shutil.rmtree(destination)
            raise
    return manifest["createdUtc"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=pathlib.Path)
    parser.add_argument("destination", type=pathlib.Path)
    args = parser.parse_args()
    print("Restored recovery point:", restore(args.archive, args.destination))
    print("Use this directory as Homelab__DataPath. Sign in with the original owner account, review inventory, then re-enroll integration credentials.")
