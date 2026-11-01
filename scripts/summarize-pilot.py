"""Summarize local pilot JSONL evidence; never automatically approve release."""
import argparse
from datetime import datetime
import json
from pathlib import Path


def summarize(paths, expected_drives=13):
    rows, errors = [], []
    for path in paths:
        for line_number, line in enumerate(path.read_text().splitlines(), 1):
            try:
                row = json.loads(line)
                at = datetime.fromisoformat(row['atUtc'])
                if at.tzinfo is None:
                    raise ValueError('Timestamp needs timezone')
                interval = row['requestedIntervalSeconds']
                if not isinstance(interval, (int, float)) or isinstance(interval, bool) or not 0 < interval <= 300:
                    raise ValueError('Pilot requires an interval from 1 to 300 seconds')
                for key in ['appResidentMemoryBytes', 'databaseAndWalBytes', 'driveCount']:
                    value = row.get(key)
                    if value is not None and (not isinstance(value, int) or isinstance(value, bool) or value < 0):
                        raise ValueError('Invalid measurement')
                rows.append((at, row))
            except (KeyError, TypeError, ValueError):
                errors.append({'file': path.name, 'line': line_number})
    rows.sort(key=lambda item: item[0])
    gaps = []
    duplicates = 0
    for (previous_at, previous), (at, current) in zip(rows, rows[1:]):
        elapsed = (at - previous_at).total_seconds()
        if elapsed == 0:
            duplicates += 1
        if elapsed > previous['requestedIntervalSeconds'] * 1.5:
            gaps.append({'fromUtc': previous_at.isoformat(), 'toUtc': at.isoformat(), 'elapsedSeconds': elapsed})
    memory = [r['appResidentMemoryBytes'] for _, r in rows if r.get('appResidentMemoryBytes') is not None and r.get('processStatus') == 'observed']
    storage = [(at, r['databaseAndWalBytes']) for at, r in rows if r.get('databaseAndWalBytes') is not None and r.get('databaseReadStatus') == 'observed']
    duration = (rows[-1][0] - rows[0][0]).total_seconds() if len(rows) > 1 else 0
    missing_memory = len(rows) - len(memory)
    missing_storage = len(rows) - len(storage)
    workload_mismatch = sum(r.get('driveCount') != expected_drives for _, r in rows)
    local_coverage = duration >= 7 * 86400 and not (errors or gaps or duplicates or missing_memory or missing_storage or workload_mismatch)
    return {
        'schemaVersion': 1, 'validSamples': len(rows), 'malformedRecords': errors, 'duplicateTimestamps': duplicates,
        'firstSampleUtc': rows[0][0].isoformat() if rows else None, 'lastSampleUtc': rows[-1][0].isoformat() if rows else None,
        'spanSeconds': duration, 'gapCount': len(gaps), 'gaps': gaps,
        'missingMemorySamples': missing_memory, 'missingStorageSamples': missing_storage,
        'expectedDriveCount': expected_drives, 'workloadMismatchSamples': workload_mismatch,
        'peakApiResidentMemoryBytes': max(memory) if memory else None,
        'peakDatabaseAndWalBytes': max(value for _, value in storage) if storage else None,
        'databaseAndWalNetChangeBytes': storage[-1][1] - storage[0][1] if len(storage) > 1 else None,
        'sevenDayLocalCoverageComplete': local_coverage,
        'apiMemoryTargetExceeded': max(memory) > 512 * 1024 * 1024 if memory else None,
        'databaseAndWalBudgetExceeded': max(value for _, value in storage) > 5 * 1024**3 if storage else None,
        'releaseApproved': False,
        'remainingEvidence': ['Collector traffic and NAS load impact', 'Retention and outage-recovery behavior', 'Operator review of workload and measurement definitions'],
        'limitations': ['Coverage uses actual timestamps; gaps exceed 1.5 times the preceding requested interval.',
                       'A seven-day span alone does not prove continuous sampling.',
                       'API RSS excludes other processes; database/WAL length is not telemetry-only storage.',
                       'Memory peaks are sampled, not continuous maxima. Storage net change is not a forecast.',
                       'Local coverage does not close all release gates.']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('files', nargs='+', type=Path)
    parser.add_argument('--expected-drives', type=int, default=13)
    args = parser.parse_args()
    if args.expected_drives <= 0:
        parser.error('Expected drive count must be positive')
    print(json.dumps(summarize(args.files, args.expected_drives), indent=2))


if __name__ == '__main__':
    main()
