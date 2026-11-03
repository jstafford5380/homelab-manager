#!/usr/bin/python3
"""Root-owned forced SSH command. No shell, tests, wakeups, or arbitrary devices."""
import os
import re
import subprocess
import sys

SMARTCTL = "/usr/sbin/smartctl"
SUDO = "/usr/bin/sudo"
DEVICE = re.compile(r"/dev/[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\Z")


def command(original, scanned):
    if original == "smartctl --scan":
        return [SUDO, "-n", SMARTCTL, "--scan"]
    prefix = "smartctl --json -x -n standby "
    if not original.startswith(prefix):
        raise ValueError("Command refused")
    device = original[len(prefix):]
    if not DEVICE.fullmatch(device) or device not in scanned:
        raise ValueError("Device refused")
    return [SUDO, "-n", SMARTCTL, "--json", "-x", "-n", "standby", device]


def devices(scan):
    return {line.split()[0] for line in scan.splitlines()
            if line.strip() and DEVICE.fullmatch(line.split()[0])}


def main():
    original = os.environ.get("SSH_ORIGINAL_COMMAND", "")
    try:
        if original == "smartctl --scan":
            args = command(original, set())
        else:
            # Validate the grammar before doing even a read-only scan.
            command(original, {original.removeprefix("smartctl --json -x -n standby ")})
            scan = subprocess.run([SUDO, "-n", SMARTCTL, "--scan"], capture_output=True,
                                  text=True, timeout=20, check=False, env={"PATH": "/usr/sbin:/usr/bin"})
            if scan.returncode != 0 or len(scan.stdout) > 65536:
                raise ValueError("Scan unavailable")
            args = command(original, devices(scan.stdout))
        result = subprocess.run(args, timeout=60, check=False, env={"PATH": "/usr/sbin:/usr/bin"})
        return result.returncode
    except (ValueError, subprocess.TimeoutExpired):
        print("SMART read refused or timed out.", file=sys.stderr)
        return 126


if __name__ == "__main__":
    sys.exit(main())
