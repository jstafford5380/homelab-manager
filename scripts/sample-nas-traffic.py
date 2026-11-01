"""Sample macOS counters for an existing NAS TCP connection; opens no network connection."""
import argparse
import csv
from datetime import datetime, timezone
import hashlib
import importlib.util
import io
import ipaddress
import json
import os
from pathlib import Path
import subprocess
import time

spec = importlib.util.spec_from_file_location('pilot', Path(__file__).with_name('sample-pilot.py'))
pilot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pilot)


def parse_counters(text, address, port):
    rows = csv.reader(io.StringIO(text))
    header = next(rows, [])
    if 'bytes_in' not in header or 'bytes_out' not in header:
        raise ValueError('Missing byte columns')
    incoming, outgoing = header.index('bytes_in'), header.index('bytes_out')
    destination = f'{address}:{port}'
    sockets = []
    for row in rows:
        if not row or '<->' not in row[0]:
            continue
        local, remote = row[0].split('<->', 1)
        if remote != destination:
            continue
        if len(row) <= max(incoming, outgoing) or not row[incoming].isdigit() or not row[outgoing].isdigit():
            raise ValueError('Invalid NAS socket counters')
        sockets.append({'socketIdentityHash': hashlib.sha256(row[0].encode()).hexdigest(),
                        'observedBytesReceived': int(row[incoming]), 'observedBytesSent': int(row[outgoing])})
    return sockets


def sample(pid, address, port, expected_identity):
    result = {'schemaVersion': 1, 'atUtc': datetime.now(timezone.utc).isoformat(),
              'status': 'unavailable', 'sockets': [], 'processIdentityHash': None,
              'source': 'macOS nettop bytes_in/bytes_out, selected API PID and exact remote IP/port',
              'limitations': ['Counters belong to currently observed sockets; no complete-interval total is inferred.',
                             'Closed sockets can disappear between samples. Reconnects and counter decreases need separate continuity review.',
                             'These are OS socket counters, not application JSON lengths or a packet capture. NAS load impact is not measured.']}
    before = pilot.process_sample(pid)['processIdentityHash']
    if not before or before != expected_identity:
        result['status'] = 'process-unavailable-or-changed'
        return result
    try:
        response = subprocess.run(['nettop', '-n', '-m', 'tcp', '-p', str(pid), '-L', '1', '-x', '-J', 'bytes_in,bytes_out'], capture_output=True, text=True, timeout=10, check=True)
        if pilot.process_sample(pid)['processIdentityHash'] != before:
            result['status'] = 'process-unavailable-or-changed'
            return result
        sockets = parse_counters(response.stdout, address, port)
        result.update(status='observed' if sockets else 'no-matching-socket', sockets=sockets, processIdentityHash=before)
    except (OSError, ValueError, subprocess.SubprocessError):
        pass
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pid', type=pilot.positive, required=True)
    parser.add_argument('--nas-address', type=ipaddress.IPv4Address, required=True, help='Exact connected IPv4 address, not a hostname')
    parser.add_argument('--port', type=pilot.positive, default=443)
    parser.add_argument('--output', type=Path, required=True, help='New output file; existing files are never overwritten')
    parser.add_argument('--samples', type=pilot.positive, default=1)
    parser.add_argument('--interval-seconds', type=pilot.positive, default=300)
    args = parser.parse_args()
    if args.port > 65535:
        parser.error('Port must be at most 65535')
    identity = pilot.process_sample(args.pid)['processIdentityHash']
    if identity is None:
        parser.error('The selected process is unavailable')
    fd = os.open(args.output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'w', encoding='utf-8') as stream:
        for index in range(args.samples):
            row = sample(args.pid, str(args.nas_address), args.port, identity)
            row['requestedIntervalSeconds'] = args.interval_seconds
            stream.write(json.dumps(row) + '\n')
            stream.flush()
            if index + 1 < args.samples:
                time.sleep(args.interval_seconds)
    print('Socket samples recorded; reconnect gaps and NAS load remain separate evidence.')


if __name__ == '__main__':
    main()
