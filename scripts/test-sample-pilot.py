from contextlib import closing
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('pilot', Path(__file__).with_name('sample-pilot.py'))
pilot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pilot)


class PilotTests(unittest.TestCase):
    def test_missing_database_is_not_created_and_missing_process_is_unknown(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'missing.db'
            with patch.object(pilot.subprocess, 'run', side_effect=OSError()):
                row = pilot.sample(path, 123)
            self.assertFalse(path.exists())
            self.assertIsNone(row['appResidentMemoryBytes'])
            self.assertIsNone(row['databaseAndWalBytes'])
            self.assertEqual(row['databaseReadStatus'], 'unavailable')

    def test_read_only_counts_exclude_absent_inventory_and_secrets(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'data.db'
            with closing(sqlite3.connect(path)) as db:
                db.executescript("""CREATE TABLE Disks(Present INTEGER, Serial TEXT);
                    INSERT INTO Disks VALUES (1,'PRIVATE-SERIAL'),(0,'ABSENT-SERIAL');
                    CREATE TABLE Assets(Type TEXT, Archived INTEGER);
                    INSERT INTO Assets VALUES ('service',0),('service',1),('host',0);
                    CREATE TABLE Capabilities(Name TEXT, Status TEXT, LastSuccessUtc TEXT, ExpectedSeconds INTEGER);
                    INSERT INTO Capabilities VALUES ('pools','supported','2026-11-01 12:00:00',60);
                    CREATE TABLE Secrets(Value TEXT); INSERT INTO Secrets VALUES ('PRIVATE-KEY');""")
            before = path.read_bytes()
            row = pilot.sample(path, os.getpid())
            self.assertEqual(row['databaseReadStatus'], 'observed')
            self.assertEqual(row['driveCount'], 1)
            self.assertEqual(row['serviceCount'], 1)
            self.assertGreater(row['appResidentMemoryBytes'], 0)
            self.assertEqual(path.read_bytes(), before)
            self.assertNotIn('PRIVATE', json.dumps(row))
            self.assertIsNone(row['collectorBytesSent'])
            self.assertEqual(row['coverage'][0]['expectedSeconds'], 60)

    def test_cli_rejects_database_output_and_appends_valid_json(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'data.db'
            path.write_bytes(b'preserve this file')
            base = [sys.executable, str(Path(pilot.__file__)), '--database', str(path), '--pid', str(os.getpid())]
            result = subprocess.run(base + ['--output', str(path)], capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(path.read_bytes(), b'preserve this file')
            output = Path(directory) / 'samples.jsonl'
            for _ in range(2):
                result = subprocess.run(base + ['--output', str(output)], capture_output=True)
                self.assertEqual(result.returncode, 0)
            rows = [json.loads(line) for line in output.read_text().splitlines()]
            self.assertEqual(len(rows), 2)
            self.assertTrue(all(r['databaseReadStatus'] == 'unavailable' for r in rows))
            self.assertEqual(output.stat().st_mode & 0o777, 0o600)

    def test_reused_process_id_never_supplies_memory_for_original_process(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / 'samples.jsonl'
            args = ['sampler', '--database', str(Path(directory) / 'missing.db'), '--pid', '123', '--output', str(output), '--samples', '2', '--interval-seconds', '1']
            rows = [dict(appResidentMemoryBytes=100, processIdentityHash=identity, processStatus='observed') for identity in ['original','replacement']]
            with patch.object(sys, 'argv', args), patch.object(pilot, 'sample', side_effect=rows), patch.object(pilot.time, 'sleep'):
                pilot.main()
            second = json.loads(output.read_text().splitlines()[1])
            self.assertIsNone(second['appResidentMemoryBytes'])
            self.assertEqual(second['processStatus'], 'identity-changed')


if __name__ == '__main__':
    unittest.main()
