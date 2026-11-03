# Restricted SMART reads (001.17)

The adapter is off by default. It never configures a NAS, installs keys, or grants permissions. Enable it only after server restrictions and sleeping-drive behavior are verified on the installed patch. The API credential remains read-only.

## Server setup

Use a dedicated key-only account, e.g. `homelab-monitor`, with no administrator groups, password SSH, or unrestricted keys. Restrict it to the app's source address in SSH/firewall policy. Install [the wrapper](../scripts/smart-read-wrapper.py) at a root-owned persistent location such as `/mnt/admin-tools/smart-read-wrapper.py`. Neither it nor its parent directories may be writable by the monitor user. Check the script's absolute Python, sudo and smartctl paths.

The authorized key must have these restrictions (substitute your address, path and public key):

```text
from="192.0.2.10",restrict,command="/usr/bin/python3 /mnt/admin-tools/smart-read-wrapper.py" ssh-ed25519 PUBLIC_KEY
```

`restrict` disables PTY, forwarding and user rc execution. The forced wrapper accepts exactly `smartctl --scan` and `smartctl --json -x -n standby /dev/<disk>`. It checks the device against a fresh scan. No shell interprets the command. Other flags, self-tests, arbitrary devices and command separators are refused.

Allow only these passwordless sudo commands. The second uses sudo's anchored argument regular expression; a wildcard could allow additional arguments:

```text
/usr/sbin/smartctl --scan
/usr/sbin/smartctl ^--json -x -n standby /dev/[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$
```

Verify the installed sudo supports argument regular expressions and `sudo -l -U homelab-monitor` lists only those reads. Never allow all sudo commands. The wrapper adds the stricter fresh-scan device check.

## TrueNAS 24.10

The [24.10 documentation](https://cdn.truenas.com/docs/scale/24.10/printview/scaletutorials/) describes public keys and limited passwordless sudo entries. In the [24.10.2 middleware source](https://github.com/truenas/middleware/blob/release/24.10.2/src/middlewared/middlewared/plugins/account.py), `update_sshpubkey` writes supplied text to authorized_keys, preserving key options. This establishes middleware support; the installed UI's validation still needs checking.

If the UI rejects or strips options, an administrator must install the restricted line through an independently secured setup channel, or configure an sshd Match block with ForceCommand and forwarding restrictions. Do not substitute an unrestricted key. Recheck restrictions, ownership and persistence after user edits or upgrades.

Using the restricted account, verify that both permitted reads work, shell and test commands fail, and a sleeping drive stays asleep. Automated tests cover command grammar and skip handling; they do not establish hardware power-state behavior.

## Application configuration

Use ASP.NET configuration, e.g. an owner-only mounted configuration file. The process needs system OpenSSH. An independently verified known_hosts file is mandatory; host-key checking cannot be disabled.

```json
{"Homelab":{"SmartEnabled":false,"SmartSsh":{"Servers":[{
  "ConnectionId":"00000000-0000-0000-0000-000000000000",
  "Host":"nas.example","Port":22,"User":"homelab-monitor",
  "KeyPath":"/run/secrets/smart_ssh_key",
  "KnownHostsPath":"/run/secrets/smart_known_hosts",
  "IntervalHours":6,"ConfirmAtaCounterUnits":false
}]}}}
```

Replace ConnectionId with the enrolled server's ID. Keep SmartEnabled false until the backend is merged, tested and setup checks pass. IntervalHours accepts 6–12 hours. To reuse an existing externally managed tunnel, use its reachable host/port and independently verify the host key for that endpoint. The reader does not create tunnels or reuse API credentials.

ATA counters stay unknown until their documented units are explicitly confirmed. Failed reads retain the old report with its age. Sleeping drives retain their last reading and are never retried without `-n standby`. Unknown counter continuity is drawn as separate points, never as evidence of repair. No command starts tests or maintenance.
