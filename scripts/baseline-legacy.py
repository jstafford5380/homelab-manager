"""Offline, exact-schema baseline for the first pre-migration development build."""
import argparse
import datetime
import pathlib
import sqlite3


def schema(connection):
    result = {}
    for (name,) in connection.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'"):
        quoted = '"' + name.replace('"', '""') + '"'
        columns = list(connection.execute("PRAGMA table_info(" + quoted + ")"))
        foreign_keys = list(connection.execute("PRAGMA foreign_key_list(" + quoted + ")"))
        indexes = sorted((row[0], row[1]) for row in connection.execute("SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name=? AND sql IS NOT NULL", (name,)))
        result[name] = (columns, foreign_keys, indexes)
    return result


def baseline(path):
    if not path.is_file():
        raise ValueError("Database does not exist")
    reference = sqlite3.connect(":memory:")
    reference.executescript(pathlib.Path(__file__).with_name("initial-schema.sql").read_text(encoding="utf-8-sig"))
    source = sqlite3.connect(path)
    try:
        if source.execute("SELECT COUNT(*) FROM sqlite_master WHERE name='__EFMigrationsHistory'").fetchone()[0]:
            raise ValueError("Database is already migration-managed")
        if schema(source) != schema(reference):
            raise ValueError("Schema differs from the original development build; no changes made")
        backup_path = path.with_name(path.name + ".before-baseline-" + datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%S%f") + ".bak")
        with sqlite3.connect(backup_path) as backup:
            source.backup(backup)
        source.execute("BEGIN IMMEDIATE")
        source.execute(reference.execute("SELECT sql FROM sqlite_master WHERE name='__EFMigrationsHistory'").fetchone()[0])
        source.executemany('INSERT INTO "__EFMigrationsHistory" VALUES (?, ?)', reference.execute('SELECT * FROM "__EFMigrationsHistory"').fetchall())
        source.commit()
        return backup_path
    finally:
        source.close()
        reference.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database", type=pathlib.Path)
    parser.add_argument("--service-stopped", action="store_true", required=True, help="Confirm all application instances are stopped")
    args = parser.parse_args()
    print("Baseline recorded. Consistent backup:", baseline(args.database))
