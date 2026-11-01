"""Record local pilot measurements without contacting the NAS or reading credentials."""
from contextlib import closing
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import time


def process_sample(pid):
    try:
        identity = subprocess.run(['ps', '-p', str(pid), '-o', 'lstart=', '-o', 'comm='], capture_output=True, text=True, timeout=3, check=True).stdout.strip()
        rss = subprocess.run(['ps', '-p', str(pid), '-o', 'rss='], capture_output=True, text=True, timeout=3, check=True).stdout.strip()
        check_identity = subprocess.run(['ps', '-p', str(pid), '-o', 'lstart=', '-o', 'comm='], capture_output=True, text=True, timeout=3, check=True).stdout.strip()
        if not identity or identity != check_identity or not rss.isdigit():
            raise ValueError('Process changed during measurement')
        return {'appResidentMemoryBytes': int(rss) * 1024, 'processIdentityHash': hashlib.sha256(identity.encode()).hexdigest(), 'processStatus': 'observed'}
    except (OSError, ValueError, subprocess.SubprocessError):
        return {'appResidentMemoryBytes': None, 'processIdentityHash': None, 'processStatus': 'unavailable'}


def file_size(path, absent_zero=False):
    try:
        return path.stat().st_size
    except FileNotFoundError:
        return 0 if absent_zero else None
    except OSError:
        return None


def sample(database, pid):
    database = database.resolve()
    result = {'schemaVersion': 1, 'atUtc': datetime.now(timezone.utc).isoformat(), 'pid': pid,
              **process_sample(pid), 'databaseBytes': file_size(database), 'walBytes': file_size(Path(str(database) + '-wal'), True),
              'collectorBytesSent': None, 'collectorBytesReceived': None, 'nasLoadImpactEvidenceReference': None,
              'databaseReadStatus': 'unavailable', 'coverage': [], 'driveCount': None, 'serviceCount': None,
              'limitations': ['RSS measures only the selected process, not Aspire, the development frontend, containers or total host memory.',
                             'Database plus WAL is a conservative local file-size measurement, not telemetry-only storage or allocated disk blocks.',
                             'Collector traffic and NAS load impact are not measured. Missing measurements are not zero.']}
    sizes = (result['databaseBytes'], result['walBytes'])
    result['databaseAndWalBytes'] = sum(sizes) if all(x is not None for x in sizes) else None
    # SQLite read-only mode must fail for an absent database, never create one.
    try:
        with closing(sqlite3.connect(database.as_uri() + '?mode=ro', uri=True, timeout=2)) as db:
            db.execute('PRAGMA query_only=ON')
            with db:
                result['driveCount'] = db.execute('SELECT COUNT(*) FROM Disks WHERE Present=1').fetchone()[0]
                result['serviceCount'] = db.execute("SELECT COUNT(*) FROM Assets WHERE Type='service' AND Archived=0").fetchone()[0]
                result['coverage'] = [dict(zip(['name', 'status', 'lastSuccessUtc', 'expectedSeconds', 'count'], row)) for row in db.execute('SELECT Name,Status,LastSuccessUtc,ExpectedSeconds,COUNT(*) FROM Capabilities GROUP BY Name,Status,LastSuccessUtc,ExpectedSeconds')]
                result['databaseReadStatus'] = 'observed'
    except (sqlite3.Error, OSError):
        result['databaseReadStatus'] = 'unavailable'
        result['driveCount'] = result['serviceCount'] = None
        result['coverage'] = []
    return result


def positive(value):
    number = int(value)
    if number <= 0:
        raise argparse.ArgumentTypeError('Must be positive')
    return number


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--database', type=Path, required=True)
    parser.add_argument('--pid', type=positive, required=True, help='Homelab API process ID; a stopped/restarted process becomes a gap')
    parser.add_argument('--output', type=Path, required=True, help='Append-only JSONL file; do not use the application database path')
    parser.add_argument('--samples', type=positive, default=1)
    parser.add_argument('--interval-seconds', type=positive, default=300)
    args = parser.parse_args()
    database = args.database.resolve()
    output = args.output.resolve()
    if output in [database, Path(str(database) + '-wal'), Path(str(database) + '-shm')]:
        parser.error('Output must be separate from the database and its sidecar files')
    fd = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o600)
    with os.fdopen(fd, 'a', encoding='utf-8') as stream:
        baseline_identity = None
        for index in range(args.samples):
            row = sample(database, args.pid)
            if index == 0:
                baseline_identity = row['processIdentityHash']
            elif row['processIdentityHash'] is not None and row['processIdentityHash'] != baseline_identity:
                row['appResidentMemoryBytes'] = None
                row['processStatus'] = 'identity-changed'
            row['requestedIntervalSeconds'] = args.interval_seconds
            stream.write(json.dumps(row) + '\n')
            stream.flush()
            if index + 1 < args.samples:
                time.sleep(args.interval_seconds)
    print(f'Recorded {args.samples} local sample(s). This does not approve the pilot gate.')


if __name__ == '__main__':
    main()
