# Homelab Control

React + TypeScript + shadcn/ui, hosted by ASP.NET Core 10 and orchestrated with Aspire 13.6.4.

## Deploy from GitHub with Coolify

1. In Coolify, create an **Application** from the GitHub repository and select the **Docker Compose** build pack. Select your branch, use the repository root as the base directory, and set **Docker Compose Location** to `/compose.coolify.yaml`. Load the Compose file and use Coolify's managed deployment mode.
2. In **Domains**, assign an HTTPS domain to the `homelab` service. Enter `https://homelab.example.com:8080`, replacing the hostname with yours. The suffix selects the container port. Open the deployed app at `https://homelab.example.com`. Point the hostname at your Coolify proxy and let it issue the public TLS certificate. [Coolify Compose and domains](https://coolify.io/docs/applications/builds/docker-compose)
3. In **Environment Variables**, set the following values. Enable **Runtime Variable** and clear **Build Variable** for each. Store the token, PFX and password as secrets.

   | Variable | Value |
   | --- | --- |
   | `HOMELAB_SETUP_TOKEN` | A strong random token for creating the first owner. Generate one with `openssl rand -hex 32`. |
   | `HOMELAB_KEY_CERTIFICATE_BASE64` | A single-line Base64 encoding of the password-protected credential-encryption PFX. |
   | `HOMELAB_KEY_CERTIFICATE_PASSWORD` | The password used to export that PFX. |
   | `HOMELAB_TRUSTED_PROXY` | The Coolify proxy's IP address on the Docker network shared with this app, or that network's CIDR if every attached peer is trusted. |
   | `HOMELAB_HARDWARE_METRICS_ENABLED` | Optional. Set to `true` to collect read-only TrueNAS hardware metrics. Defaults to `false`. After redeploying, use **Refresh** on the Hardware page to collect immediately. |

   Create the credential-encryption PFX once on your workstation, in a private working directory. Use a strong PFX password such as one generated with `openssl rand -hex 32`. Paste the final command's output into the Base64 variable and keep the matching password.

   ```sh
   umask 077
   openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=Homelab credential protection" -keyout homelab-key.pem -out homelab-key.crt
   openssl pkcs12 -export -inkey homelab-key.pem -in homelab-key.crt -out homelab-key.pfx
   openssl base64 -A -in homelab-key.pfx
   ```

   The app accepts HTTPS forwarding headers from the configured proxy address or network. If the app network is created on the first deployment, deploy with this variable blank, then find the shared network and proxy address on the Coolify Docker host:

   ```sh
   docker inspect coolify-proxy --format '{{range $name, $net := .NetworkSettings.Networks}}{{println $name $net.IPAddress}}{{end}}'
   ```

   Use the entry for the network shared with the Homelab container, set the variable, and redeploy. Use a trusted network CIDR if the proxy IP changes on recreation. The API requires HTTPS and will reject requests through an unconfigured proxy. [ASP.NET Core proxy configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)

4. Select **Deploy**. Coolify builds the image and creates named volumes for `/data` and `/backups`. Open the HTTPS domain and create the owner using the setup token. Remove the token variable and redeploy after setup. Add the NAS in **Settings** using its API endpoint and certificate trust details.

Redeploy the same Coolify application to keep its volumes. Preserve the PFX and password across deployments so the app can reopen its encrypted key ring. The backup volume starts on the Docker host. Copy backup archives to independent storage for recovery from host loss. For startup errors, check Coolify's **Deployment Logs** and **Runtime Logs**. If the page loads but API calls say HTTPS is required, check the domain's HTTPS configuration and the proxy address on the shared Docker network.

## Local Docker Compose setup

The included [compose.yaml](compose.yaml) builds one container that serves both the web interface and API. It uses SQLite; no separate database, Node.js installation, .NET installation or Aspire dashboard is needed on the Docker host.

### Requirements

- Docker Engine with the `docker compose` plugin, or Docker Desktop configured for Linux containers. Check `docker version` and `docker compose version` before starting.
- Python 3 and OpenSSL on the host, plus `curl` for the readiness check. The preparation script needs an OpenSSL build supporting `req -addext` and `pkcs12 -export`.
- A regular, non-root Linux/macOS user with Docker access. On Windows, run the preparation commands inside WSL2 with Docker integration enabled; the script uses Unix user/group IDs and file permissions.

Run all commands below from the **repository root**. Run preparation as the same user who will manage the deployment, without `sudo`.

### First start

1. Prepare the local folders, certificates and setup token:

   ```sh
   python3 scripts/prepare-local-deployment.py
   ```

   This creates `.env`, `data/`, `backups/` and `secrets/`. `.env` records your `HOMELAB_UID` and `HOMELAB_GID` so the container can read its secret files and write its data as your user. The script refuses to run if `.env` or `secrets/` already exists. Do not create `.env` first, rerun preparation for upgrades, or delete existing secrets to get past this check.

2. Optionally change the host port by adding `HOMELAB_HTTPS_PORT=9443` to the generated `.env`. The default is **8443**. Leave the generated UID/GID values intact unless you are deliberately changing file ownership.

3. Validate the configuration, build and start:

   ```sh
   docker compose config --quiet
   docker compose up -d --build
   docker compose ps
   ```

   The first build downloads the Node/.NET build images and dependencies, builds the frontend, and publishes the API. The final container runs the combined application in Production mode. Subsequent builds can reuse Docker's cache.

4. Check the generated certificate's fingerprint, then check readiness using that certificate:

   ```sh
   openssl x509 -in secrets/tls.crt -noout -sha256 -fingerprint
   curl --cacert secrets/tls.crt https://localhost:8443/health/ready
   ```

   Readiness returns `{"status":"ready"}` after startup and database migrations finish. Substitute your chosen port if it is not 8443. Open **https://localhost:8443** in your browser. The generated certificate is self-signed: inspect its fingerprint and trust that specific local certificate before entering credentials. The public certificate is `secrets/tls.crt`; never import or share the PFX/password files as browser trust certificates. The default port is bound to **127.0.0.1**, so it is reachable only from the Docker host.

5. Read the temporary setup token locally:

   ```sh
   cat secrets/setup-token
   ```

   Use it on the setup screen to create the owner username and password. The setup token is not your sign-in password. After successful account creation, clear the token's contents while keeping the file in place for the Compose secret mount:

   ```sh
   : > secrets/setup-token
   ```

   Future sign-ins use the owner account. Do not delete the token file while `compose.yaml` still references it. An existing owner prevents setup from being run again.

6. In **Settings**, add your NAS using its logical `wss://nas.example/websocket` address, API key and public certificate trust material. The container must be able to resolve and reach the NAS. See [Connections](#connections) for TLS and tunnel details. Optional dataset, cached-temperature and power-on-hours reads are enabled separately in Settings; saving a connection does not enable every optional collector.

### Persistent files

| Host path | Container location | Purpose |
| --- | --- | --- |
| `data/` | `/data` | SQLite database, key ring in `data/keys`, and pre-migration database backups |
| `backups/` | `/backups` | Application backup ZIP files when backups are configured/enabled in Settings |
| `secrets/key-encryption.pfx` and `secrets/key-encryption.password` | `/run/secrets/key_certificate` and `/run/secrets/key_password` | Encrypt/decrypt the persisted credential-protection key ring |
| `secrets/tls.pfx` and `secrets/tls.password` | `/run/secrets/tls_certificate` and `/run/secrets/tls_password` | Serve HTTPS |
| `secrets/setup-token` | `/run/secrets/setup_token` | Initial owner enrollment; may be empty after setup |
| `.env` | Used by Compose on the host | Host UID/GID and optional published HTTPS port |

The certificates serve different purposes: replacing the HTTPS certificate does not require replacing the key-encryption certificate. The generated HTTPS certificate lasts one year; the generated key-encryption certificate lasts ten years. Keep the key ring and its matching key-encryption PFX/password available across container rebuilds. Follow [Production deployment security](#production-deployment-security) for certificate rotation.

These are host bind mounts, so replacing the container preserves the folders. Keep their paths and ownership consistent when moving the checkout or host. Compose uses **root `data/`**; development normally uses **`src/Api/Homelab.Api/data/`**. Starting Compose does not automatically migrate a development database or its keys. Use the [backup/restore procedure](#application-backups-and-restore) when moving an existing installation; do not run two app instances against one database.

The default `backups/` directory is on the same host. For independent backups, replace its volume source in `compose.yaml` with your mounted backup destination, for example `/mnt/independent/homelab-backups:/backups`, and give the configured UID/GID write access. Then configure retention and enable backups in Settings.

### Access from another computer

The supplied Compose file and generated certificate are for host-local HTTPS. To serve your LAN hostname:

1. Supply a TLS PFX whose certificate covers that hostname, with its password in the configured secret file. Replace the TLS files only; retain the credential-encryption files and key ring.
2. Change the published address in `compose.yaml` from `127.0.0.1` to the intended host LAN interface, and allow the chosen port through that host's firewall.
3. Recreate the container with `docker compose up -d --force-recreate homelab` to reload the mounted certificate, then use `https://your-hostname:PORT` from clients that trust its certificate.

`localhost` on another computer refers to that computer. A reverse proxy also needs a valid HTTPS connection to the app: the production API rejects plain HTTP requests. Keep the production settings rather than switching the container to Development to work around a certificate or networking problem.

### Updates and routine commands

Create an application backup before upgrading. Update your checkout to the desired revision, preserve `.env`, `data/`, `backups/` and `secrets/`, then run:

```sh
docker compose up -d --build
docker compose logs --tail=100 homelab
```

Startup backs up an existing database before applying pending migrations. A rollback needs the matching application version and database backup; an older image alone cannot undo a schema upgrade.

```sh
docker compose logs -f --tail=100 homelab
docker compose stop homelab
docker compose start homelab
```

Use `docker compose up -d` after changing Compose settings, ports or environment variables; `docker compose restart` alone does not apply a new container configuration. The source checkout defines the image, so rebuilding with `--build` is needed after application changes. Refer to [Docker's Compose command reference](https://docs.docker.com/reference/cli/docker/compose/up/) and [file-based secret documentation](https://docs.docker.com/compose/how-tos/use-secrets/) for those lifecycle and mount semantics.

### Troubleshooting

| Symptom | Check |
| --- | --- |
| Compose says `HOMELAB_UID` or `HOMELAB_GID` is missing | Run preparation once from the checkout, then run Compose from the repository root so it reads the generated `.env`. |
| Preparation refuses existing `.env` or `secrets/` | Reuse a complete existing deployment. If an earlier preparation failed, inspect and preserve its files before repairing it; the script intentionally does not overwrite them. |
| Container cannot read a secret or write `/data`/`/backups` | Check host ownership and permissions against `.env`. File-based Compose secrets retain host-file access restrictions. Avoid opening permissions broadly or running the service as root. |
| Browser reports a certificate error | Check the TLS certificate's hostname, fingerprint, validity and client trust. The generated certificate covers only `localhost` and `127.0.0.1`. |
| Container starts but readiness fails | Inspect `docker compose logs --tail=100 homelab` for certificate loading, volume access or database upgrade errors. Preserve migration backups before investigating a schema problem. |
| Connection cannot reach the NAS or a host-owned tunnel | Test the NAS hostname's Docker DNS/network reachability. Inside the container, `localhost` is the container. For a host-owned tunnel use a reachable host endpoint such as the provided `host.docker.internal` mapping; the tunnel must listen on an interface reachable from Docker. TLS still validates the logical NAS hostname. |
| Compose shows an empty inventory after using development | The two modes use different data directories. Move an existing installation through the backup/restore workflow. |

The base Compose file does not configure optional SSH SMART access. That reader additionally needs an OpenSSH client in its image, restricted key/known-host mounts, and the NAS-side restrictions described in [SMART SSH setup](docs/SMART_SSH_SETUP.md). Development-only feature settings are not automatically enabled in Production; configure optional features explicitly for the deployment.

## Development

Requires .NET 10, Node.js 24 or later, and OpenSSL for the temporary-token command below. From the repository root:

```sh
npm run install:web
dotnet dev-certs https --trust
umask 077
openssl rand -hex 32 > owner-setup-token.txt
export Homelab__SetupTokenFile="$PWD/owner-setup-token.txt"
npm run dev:aspire
```

Open **http://localhost:26264** for the UI and the dashboard address printed by Aspire for resource status/logs. Read `owner-setup-token.txt` locally and use it to create the owner account. After setup, clear the token file; it is ignored by Git. The default dashboard is HTTPS. For an HTTP dashboard, run `dotnet run --project src/Api/Homelab.AppHost --launch-profile http` instead. This AppHost profile explicitly permits unsecured Aspire transport for local development only; production HTTPS requirements remain unchanged. Aspire supplies the backend endpoint to Vite. The API persists state under `src/Api/Homelab.Api/data` by default. Do not commit data or keys. See [Stable development addresses](#stable-development-addresses) for port overrides and the distinction between fixed exposed addresses and internal process ports.

For standalone hosting, build and publish in this order:

```sh
npm run build:web
dotnet publish src/Api/Homelab.Api -c Release -o artifacts/publish
```

Frontend output is written to `src/Web/dist`; the API build copies it into its generated `wwwroot`. Publishing creates the deployable files; a production launch still requires persistent data paths, HTTPS and the [credential-protection certificate configuration](#production-deployment-security).

## Connections

Use a logical `wss://nas.example/websocket` address. For an externally managed SSH tunnel, additionally supply the TCP connection host and forwarded port. TLS still validates the logical NAS hostname. Use OS certificate trust or paste the NAS public CA/self-signed certificate during enrollment (or later in Settings → Manage connection). Both modes validate the logical hostname; no insecure certificate bypass exists. A Docker container's localhost refers to the container; use an explicitly reachable host endpoint for host-owned tunnels.

The NAS adapter accepts the TrueNAS 24.10 patch family; other releases are paused as unsupported. The current installed target is 24.10.2.4, with its bounded source comparison documented in [the NAS read manifest](docs/NAS_READ_MANIFEST.md). Validate the installed patch and credential permissions before relying on a deployment. The collector keeps incomplete SMART/recovery coverage explicit; local use and synthetic tests do not complete the independent-host restore or reference-host pilot release gates.

## Verification

From the repository root after installing frontend dependencies. For the first browser run, install Chromium as described in [Browser checks](#browser-checks).

```sh
npm run build
npm test
npm run lint
npm run test:web
```

## Current implementation status

Implemented: Aspire orchestration, responsive React/shadcn UI, owner authentication, encrypted integration credentials, SQLite migrations, direct/tunneled read-only collection, topology and identity review, SMART imports and evidence trends, freshness and findings, tested webhook/outbox behavior, recovery policies and evidence history, inventory/spares/budgets, acquisition and replacement records, exports, and application backup/restore. Current verification and limitations are maintained in [IMPLEMENTATION_STATUS.md](IMPLEMENTATION_STATUS.md).

This is an implementation in progress, not the completed specification. Remaining work is tracked in `IMPLEMENTATION_STATUS.md`. Do not rely on this build as your sole NAS alerting or recovery system.

## Database upgrades

Stop all application instances before offline database maintenance. New databases use checked-in EF Core migrations. Startup creates a consistent SQLite backup under `data/migration-backups` before applying a pending migration. Preserve the matching application version and key material alongside backups; rollback means stopping the service and restoring the pre-upgrade database and application together.

For databases created by the initial development build (before migrations):

```sh
python3 scripts/baseline-legacy.py src/Api/Homelab.Api/data/homelab.db --service-stopped
```

The command verifies the complete table/column/index/foreign-key shape against the original schema, creates a consistent backup, and records the baseline migration without altering inventory or owner records. It refuses unknown schemas and already-managed databases. Restart the application afterward. Never run the service against the same database from two processes.

## Application backups and restore

Set `Homelab__BackupPath` to an existing writable destination outside the application's live data directory. A separate host/storage failure domain is recommended; the application cannot verify physical independence. Settings lets you enable daily backups, choose daily/weekly retention, create a backup immediately, and inspect backup history. The scheduler retries unsuccessful attempts every five minutes and never updates the last successful backup on failure.

Archives contain a consistent SQLite snapshot and a checksum/migration manifest. They include owner password hashes, private inventory, and encrypted integration credentials; restrict backup access. Decryption keys are deliberately excluded and need separate secure custody if you require credential recovery. Ordinary restoration instead clears integration credentials, disables collectors and backup scheduling, and preserves the owner account and inventory.

```sh
python3 scripts/restore-backup.py /independent/backups/homelab-....zip /new-host/homelab-data
```

The destination must not already exist. For standalone hosting, start the application with that path as `Homelab__DataPath`. For Compose, use the restored directory as the host source of the `/data` bind mount and keep `Homelab__DataPath=/data` inside the container. Sign in with the original owner account, review history, configure a new backup destination, and rotate/re-enroll each NAS credential before collecting. Use Settings → Edit connection → Rotate credential & resume. API clients must first read `/api/v1/connections`, then send an authenticated `PUT /api/v1/connections/{id}/credentials` with `{ "apiKey": "...", "configurationRevision": "the-current-revision" }` and the required `X-Homelab-Request: 1` header. A stale or omitted revision returns 409 without changing the key or enabling collection.

A same-machine isolated-directory restore is automated in the test suite. A real independent-host restore and measured RPO/RTO still require an operator exercise; the proposed targets remain 24-hour RPO and four-hour RTO.

## NAS safety contract

The NAS integration is monitor-and-advise only. Its fixed read methods and arguments are enforced both at the collector entry point and before RPC transmission. Authentication establishes the session. A separately enabled, typed ATA SMART-attribute read obtains power-on hours and can wake sleeping drives; it cannot start tests or accept arbitrary command arguments. The WebSocket integration has no shell or maintenance interface. An optional, default-off SMART reader adds a separate restricted SSH channel with exactly two read-only commands, enforced by a forced server-side wrapper and narrow sudo permissions; see [SMART SSH setup](docs/SMART_SSH_SETUP.md). It never starts tests or retries without the standby guard. Scrubs, SMART self-tests, disk offline/replace/wipe, error clearing, configuration changes, permission changes, upgrades and reboots are prohibited. Local notes, planning, recovery records and finding acknowledgments affect only this application's database. Acknowledgment never dismisses a native TrueNAS alert.

Use the narrowest NAS-side account/key permissions supported and validated for the installed release. Legacy credentials may have broader server-side authority; application enforcement remains necessary and must not be described as proof that the credential itself is read-only. No production NAS permission audit has been performed yet.

## Notifications

Daily quiet hours use the application timezone in Settings and can span midnight (for example, 22:00–07:00). Noncritical notifications wait until the range ends; critical findings bypass quiet hours. Clear both times to disable the schedule. Sending a test remains available during quiet hours.

Telegram messages summarize urgency, the finding, affected equipment, and the next step. Technical evidence and per-source collection timestamps remain in the application. A short monitoring warning appears when sources are stale or unavailable; intentionally disabled and unsupported sources do not trigger that warning. Resolved notifications identify the finding as resolved without repeating its earlier action. Identifier masking still applies to message content.

Enroll notifier hostnames using `Homelab__WebhookHosts__0=notifier.example` (and numbered entries for additional explicitly allowed hosts). In Settings, save the HTTPS webhook URL, send a test, then enable delivery. URLs are encrypted and never returned to the browser. Private identifiers are masked by default. Outbound HTTP telemetry is suppressed because webhook URL paths can contain secrets. No redirects are followed; loopback, link-local/metadata, multicast, and monitored NAS addresses are rejected. Private notifier addresses require explicit hostname enrollment too.

The queue deduplicates by channel revision, finding episode, event and severity. Network delivery and destination tests run outside the database writer lock. Claims and results are saved under short locks, with channel revisions checked again before a test can validate the destination. Changing settings cannot recall a request already sent; its receiver result stays in attempt history without reviving a cancelled delivery. The ten-second send deadline includes DNS lookup and connection setup. Delivery is at-least-once: a crash after receiver acceptance can repeat the same event identifier. Receivers should deduplicate `eventId`. Five bounded attempts are made with backoff (including claims interrupted by shutdown), and history distinguishes accepted, pending and failed. If the fifth claimed send ends without a recorded response, automatic retry stops and recent attempt details show “Receiver outcome unknown.” The receiver may already have accepted the event; a manual retry reuses its event identifier and retains prior history. Noncritical maintenance suppression lasts at most seven days; critical findings bypass it. Accepted means HTTP 2xx, not that a person read the message. Restoring an application backup disables notifications and cancels pending deliveries until the destination is re-enrolled and tested.

## Recovery evidence

The Recovery page records protection policies, actual backup outcomes and restore exercises performed by the operator. The app does not execute backups or restores on the NAS. RPO uses a destination-confirmed represented data point, never just job finish time. RTO is only demonstrated by a passed, current, matching-scope restore exercise. Local snapshots remain source-pool protection; independence is explicitly recorded. Manual evidence requires an author (the signed-in owner), observation time, evidence reference and review deadline. Editing a policy creates a new revision so old evidence cannot silently validate a changed scope or destination. Policy edits require the current `revision`; backup and restore submissions require the displayed `policyRevision`. A changed or missing revision returns HTTP 409 before any write. The form keeps the draft and refreshes the policy list; copy any notes you need, close the form and reopen it to review the current scope before resubmitting. API evidence dates must use explicit UTC timestamps on or after 1970, with the represented recovery point no later than the completed attempt/test. Review deadlines must be after the event and within the next year at submission. Evidence fields are bounded and required; optional text uses an empty string, not null. Historical malformed records remain stored but cannot establish readiness; a malformed latest restore is shown as unverified evidence rather than falling back to an older pass. Recovery runbooks and custody references are stored locally and remain accessible while the NAS is offline; enter references rather than encryption secrets. Optional destination-capacity notes should include units, the date checked and the evidence source. Restore prerequisites capture configuration backups, permissions, application state and dependencies. Both are manual planning context, not current capacity verification or proof of recoverability; blank values remain “Not recorded.” They are preserved in private inventory exports and omitted from redacted exports. Editing either creates a new policy revision like other policy changes.

## Inventory and offline exports

Inventory supports manual hosts, services, enclosures, controllers, backup destinations, power/network assets and spares. Record compatible models, stock quantities and estimated unit costs without implying that age alone requires replacement. Dependencies distinguish runs-on, stores-on, backed-up-to, powered-by and connected-to; verification status and evidence remain explicit. Archive retired assets to retain their context.

Download the inventory JSON from the Inventory page for use while the NAS or application is offline. It includes selected inventory and recovery runbook fields, not the database or credentials. A redacted option removes names, serials, locations and free-text documentation. Keep the full export private and do not put secrets into runbooks or inventory notes. The export is not a substitute for the consistent database backup.

## Production deployment security

Production requires HTTPS for API access and a certificate to encrypt the Data Protection key ring. For Coolify, supply the PFX as `Homelab__KeyCertificateBase64` and its password as `Homelab__KeyCertificatePassword` through the Compose runtime variables above. `Homelab__KeyCertificatePath` points to a password-protected PFX, with `Homelab__KeyCertificatePasswordFile` stored outside the database/key directory. `Homelab__KeyRingPath` can relocate the encrypted key ring. Keep the certificate and password in separate secure custody from backups. Development mode permits an unencrypted development key ring; never reuse it for production credentials.

Use the [Local Docker Compose setup](#local-docker-compose-setup) above for first-run commands, persistent folders, local/LAN TLS, upgrades and troubleshooting. Compose mounts certificates and passwords as files; it does not put their contents into image layers or the Compose environment.

Rotate the key-encryption certificate by preserving the previous PFX/password in secure custody, installing the new PFX, and setting `Homelab__PreviousKeyCertificates__0` to the prior PFX during transition. Previous certificates currently use the configured key-certificate password; rewrap them to the same transition password offline if needed. Retain old decryptors until all encrypted records/backups that require them have expired or been securely re-enrolled. Never delete the old key ring casually. Setup tokens may be provided via `Homelab__SetupTokenFile` and removed after account creation.

`/health/live` and `/health/ready` return minimal status in production. Configure a checker on a different host to poll the readiness URL; a stopped app cannot notify you about itself. A non-2xx response, timeout, or certificate failure should alert through that checker's independently configured channel. Do not use the monitored NAS as the checker's sole host.

Copy `scripts/check-service.py` to a host independent of both Homelab Control and the NAS. It requires only Python 3 and issues one unauthenticated GET to the explicitly configured readiness URL:

```sh
python3 check-service.py https://control.example/health/ready --ca-file /secure/trust/control-ca.pem --timeout 10
```

Omit `--ca-file` to use system certificate trust. Supply the certificate's real hostname; there is no insecure TLS switch, redirect following, HTTP fallback, proxy-environment use or NAS contact. Credentials, query strings and fragments are rejected. A 200 response must contain JSON with `status: ready`, within 4 KiB. The outer deadline covers process startup, DNS, TLS, headers and body together. Output is one JSON report with UTC check time, elapsed seconds, status and a bounded reason; it omits the endpoint and response body. Exit codes are **0 ready**, **1 unavailable/unverified**, and **2 invalid command configuration**.

Configure your independent monitoring service to execute it every 60 seconds, alert on any nonzero exit or a missing scheduled execution, and report recovery after a later successful check. Use that service's own notification channel, not Homelab Control's outbox. The script itself sends no messages and stores no credentials. Confirm channel delivery and checker-host independence before relying on it. Readiness verifies the control process and database connection; it does not certify NAS health, freshness or recovery readiness.

`python3 scripts/test-check-service.py` runs synthetic local HTTPS tests (OpenSSL is required only to create their temporary certificate). The disposable production exercise in `scripts/validate-container.py` invokes the checker from the host, stops only its temporary app container, verifies failure, then starts it and verifies recovery. This demonstrates stop detection across processes on one machine; it does not prove independent-host placement, real notification delivery or the seven-day pilot.


## Browser checks

After frontend and backend builds, run `npx playwright install chromium` and `npx playwright test` from `src/Web`. The suite starts a disposable local Development instance and never contacts a real NAS or notifier.

## Finding lifecycle

Acknowledgments assign the current owner and a seven-day review date; expiry reopens review and permits a reminder. Snoozing affects app delivery only, expires within seven days, and never suppresses a critical finding. Existing faults stay open through collection failures. Pool-state findings resolve only after two later successful ONLINE snapshots. Pending/uncorrectable media-presence findings resolve only after two later fresh zero observations with the same source, schema and explicit counter-continuity group. History is retained, and resolution does not prove backup readiness or future drive reliability.

When importing observations, use a continuity group only if the same device/source/counter baseline is established. Change it after resets, reboot boundaries or source changes. Unknown continuity disables delta comparison and automatic media resolution. No NAS maintenance command is issued by acknowledgment, snooze or resolution.

## NAS certificate trust and connection changes

Settings → Manage connection provides endpoint/tunnel edits, polling cadence, pause/resume and credential rotation. Paste only the public PEM CA/self-signed certificate; private keys are rejected. Custom trust still checks the logical endpoint hostname and certificate validity. The app records the SHA-256 fingerprint after a successful trusted authenticated connection; subsequent changes fail closed. Verify a planned replacement certificate independently and update its fingerprint explicitly. Clearing the fingerprint reenrolls the next certificate that passes trust and hostname checks. Private-CA chains are validated offline (revocation downloads are disabled); remove compromised trust material and rotate credentials explicitly.

Pausing prevents subsequent collection. An already-running read may finish, but its results are discarded when the connection configuration revision changes. No connection setting causes a NAS mutation.

### Recording a completed drive replacement

After the replacement has been performed outside Homelab Control and the new physical disk appears in collected inventory, open the previous disk under Storage and choose **Record a past replacement**. Choose the new disk, retype both serials (or WWNs when serials are absent), and record the time, date confidence, reason, operator evidence and pool/vdev context. State when membership is unknown rather than guessing a GUID.

This creates a local history link only. Each physical disk retains its own observations and open findings; the record does not retire a disk, perform NAS maintenance or assert that the replacement restored health. Both drive pages show the event. Use **Mark record incorrect** with an explanation to retain a mistaken entry as an auditable correction, then add a corrected record if needed. Replacement records are included in application backups and inventory exports; redacted exports omit identifiers and private notes.

### Inventory scheduling and evidence age

Set **Disk inventory interval (minutes)** in Settings to control how often physical-disk discovery runs. The effective interval is the greater of this setting and the connection's pool/alert poll interval. Pools and native alerts are read on each connection cycle. Editing/re-enabling a connection or retrying failed inventory triggers a new inventory read. Skipped inventory polls preserve disk presence, alias history and original last-seen timestamps; a successful pool read does not make disk inventory newer. Each coverage card shows its own last successful check. Inventory becomes stale after three effective intervals without a successful inventory read. Failed collection retains prior evidence and never proves a fault has cleared.

### Direct links to evidence

Pages have stable URLs such as `/storage`, `/planning` and `/recovery`. A physical drive can be opened at `/storage?disk=<physical-disk-id>`. Notification links using `/?finding=<finding-id>` open the saved finding evidence, including any resolution recorded since the notification was sent. These routes require owner authentication and survive reloads; signing in preserves the requested route. A finding's observation time and recording time are displayed separately. Missing records show an explicit unavailable state rather than a healthy result. Browser Back/Forward and opening navigation links in new tabs are supported.

### When the browser cannot refresh

The header reports whether the latest overview refresh succeeded. If the control service cannot provide an overview, the current browser tab retains its last valid snapshot and shows its load time with a retry action. This is last-known evidence, not a current NAS health assessment. Refreshes time out after 15 seconds; failed or superseded requests cannot replace a newer valid snapshot. On initial load, unavailable data is shown as unavailable rather than an empty installation. Browser-retained evidence is memory-only and is lost on reload; the application database remains the source of saved history when the service is available again.

### NAS hardware identity changes

The collector accepts stable TrueNAS 24.10 release strings and pauses on other or prerelease versions. It reads only the curated `system_serial` field from `system.info` for supplementary identity evidence. The field is nullable in the [TrueNAS 24.10.2 source contract](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/system/info.py). Generic motherboard placeholders are treated as unavailable. DMI serials are not guaranteed unique and do not replace TLS certificate or hostname validation.

After a usable serial baseline has been recorded, a different or missing serial pauses collection **before storage reads**, retains the old inventory and creates an identity-review finding. Verify the endpoint, physical machine and certificate. Correct an unintended endpoint and resume; for a documented hardware change on the same NAS, open connection settings, retype the saved and newly reported serials, and record independent verification evidence. The baseline change is audited locally and must be matched by the next authenticated read before the identity finding resolves. Other findings remain open. Enroll a separate connection for a genuinely different NAS. A missing/placeholder serial cannot be accepted as a replacement baseline.

### Imported measurement units and quality

Manual drive observations use sectors for ATA media counts, errors for CRC/NVMe error counts, Celsius for temperature, and hours for operating time. The form selects the unit from the metric; convert source units explicitly before import. Counts must be nonnegative whole numbers within the importer's exact numeric range. Missing values require an explanation and retain unknown quality. Measurements without observation time are unverified and appear separately under **Evidence without observation time**; they are not plotted in the selected time window. API callers must send known observation times as UTC timestamps ending in `Z`.

Counter increases compare only matching units as well as the existing source/schema/continuity checks. Automatic media-finding resolution also requires a known original evidence unit matching the later samples. Older findings whose unit metadata is unknown remain open rather than silently guessing their units.

### Evidence authors and UTC timestamps

Manual observation imports record the authenticated owner as their author. Caller-supplied author, database ID, NAS/disk association and receive time cannot override server-assigned provenance. The activity record identifies the owner, target drive and evidence source key. Historical records without author metadata are shown as unknown rather than assigned an inferred author.

Persisted application timestamps use UTC. Database reads explicitly restore UTC timezone information so API timestamps include `Z`, and the UI applies the selected display timezone once. Existing stored UTC clock values are not shifted or rewritten. The implementation follows the [EF Core guidance for restoring DateTime.Kind through value conversion](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#specify-the-datetimekind-when-reading-dates). The automated API test uses a disposable database and a disabled NAS profile; it does not contact a NAS.

### Pool history retention and rollback

Settings → **Detailed monitoring history (days)** controls the detailed pool snapshot window (default 90 days). Once an hour, the app processes up to 2,000 eligible snapshots and 2,000 expired daily groups. Cutoffs use whole UTC days. Older snapshots within the preceding two calendar years become daily groups separated by NAS connection, pool GUID, source schema, pool state, scan function and scan state. Each group preserves reading counts, first/last fetch times, per-measurement known counts, and minimum/maximum values, including scan-error peaks. Unknown readings remain unknown; repeated scan readings are not counted as separate scans. Summaries are not inputs to failure predictions or growth forecasts. Snapshots beyond both retention windows expire without creating a summary.

Storage → pool → **View older daily summaries** exposes these records, their sources, missing counts, and any backlog. Manual drive observations, inventory, replacements, findings and recovery evidence are not removed by this job. Raw NAS payloads are not retained by the current collector. Collection-run history also has bounded retention (below); future automatic drive telemetry still needs its own retention work; this is not a claim that the complete telemetry store meets the 5 GiB target.

Cleanup requires `Homelab__BackupPath` outside the live data directory. Before every destructive batch, the app creates an integrity-checked database backup using the existing backup service, even when scheduled daily backups are disabled. Missing or failed backups prevent cleanup. Aggregation and deletion share one database transaction, so a failure rolls back both and a retry does not double-count. A successful batch records an Activity event. Existing daily/weekly backup-copy limits apply after creating the fresh backup and before compaction, so failed cleanup retries do not accumulate unlimited archives. Configure the destination on storage independent of the monitored NAS; the app cannot verify its physical failure domain. If backups remain unavailable, history keeps growing. SQLite reuses freed pages; cleanup does not promise immediate file shrinkage.

Before reducing the history window, confirm the backup destination and restore procedure. Increasing the window later does not reconstruct removed detailed samples. To inspect or recover pre-cleanup evidence, stop the app, retain its current data directory, select the archive created immediately before the relevant cleanup, and run `python3 scripts/restore-backup.py /independent/backups/homelab-....zip /new-host/homelab-data` into a **new, empty destination**. Start an isolated instance against that restored directory and inspect it before replacing any current deployment. The restore disables NAS collectors and notification channels and clears their credentials; it does not replay NAS actions. Restoring an older database also rolls back newer local records, so preserve/export those before choosing a recovery point. Migration backups are additionally made before applying schema changes. Independent-host recovery and the seven-day operating-budget pilot remain release gates.

### Collection attempt history

Storage → NAS → **View collection history** shows saved attempts, reported outcomes, durations and recorded capability states. Switch to **Daily attempt summaries** for older counts and duration ranges, grouped by NAS, UTC day, outcome, capability-state pattern and expected polling intervals. Source success timestamps are not part of the group key: regular polling must not create one supposedly daily group per attempt. Unknown/malformed legacy capability records remain explicitly unknown, and invalid duration intervals do not become zero-duration successes.

The detailed monitoring history setting applies to both pool snapshots and collection attempts. The hourly maintenance job creates one pre-cleanup backup covering both, then compacts up to 2,000 rows and expires up to 2,000 daily groups per history type. Each type commits its summaries, detail deletion and audit in one transaction. If pool cleanup succeeds and collection cleanup fails, pool cleanup remains committed; the backup contains both original histories. Collection-only installations still require a successful backup before cleanup. Summary retention uses two calendar years from the attempt date. Historical findings, manual observations and recovery evidence are not removed.

Counts represent recorded attempts, not NAS uptime, downtime duration, successful SMART checks or drive health. A partial result can include unsupported or denied capabilities. A saved supported state may refer to an earlier successful read when a capability was not due on that cycle. Offline periods with no running app produce no attempts; do not infer successful monitoring from missing rows. The displayed duration covers the application's collection attempt and includes local processing and writer waits, not just NAS response latency. The same backup/isolated-restore rollback procedure above applies to collection history.

### Optional cache-only temperatures

Settings → **Read existing NAS temperature cache** enables `disk.temperatures` with the fixed arguments `[[], {"only_cached": true}]` on each connection cycle. It is off by default pending installed-patch verification. The application cannot switch this read to fresh polling or supply arbitrary options. This is separate from full SMART monitoring, which remains unsupported.

Storage → NAS → **View cached temperatures** shows the last normalized cache, its fetch time, API availability, and unknown/missing entries. Names are reported device labels, not verified physical-drive matches. The source omits observation timestamps, so these readings never enter physical-drive trends, finding-resolution rules or replacement advice. A successful empty response replaces the prior cache with an explicit empty state; a denied/malformed read preserves the old snapshot and marks the capability accordingly. Only the latest bounded snapshot is stored, so this feature adds no unbounded telemetry history. Turning it off stops future cache reads and retains the last saved evidence.

The [read manifest and source review](docs/NAS_READ_MANIFEST.md) document the pinned TrueNAS 24.10.2 contract, exact arguments, prohibited alternatives and live-validation gates. The source cache-only branch does not issue fresh drive queries and does not populate the cache. The separate **Read power-on hours through the TrueNAS API** option performs active ATA attribute reads every six hours and offers **Run now**; those reads can wake sleeping drives. Unsupported types, denied reads and unknown counters remain explicit. The restricted SSH reader is a different optional setup; installed-hardware power-state behavior still needs operator verification.

### Updating a replacement or review plan

Planning → **Edit plan** updates a plan's title, owner label, budget/currency, review time, status, and evidence notes. Review time is explicitly entered in UTC; leave it blank to clear it. Owner is a local planning label and does not change the authenticated account or send anyone a message. Budgets remain manual estimates, grouped by currency; completing a plan removes its budget from the open-plan totals without claiming that a drive issue is resolved.

Every creation, edit and status change records the authenticated author and immutable before/after snapshots in **Plan change history**. Earlier plans predate this tracking: the first edit preserves their previous state but does not invent an earlier author. These records are included in database backups and are retained until an explicit future archival workflow. A plan's linked physical drive cannot be reassigned; create a separate plan for another drive to keep evidence attached to its original context.

Saving includes the revision shown when the editor opened. If another tab or user saves first, the API returns a conflict and the editor retains your unsaved draft. Copy anything you need, close and reopen the latest plan, then reconcile the changes. The inline status selector follows the same rule. API clients must send the full plan fields and current `revision` when using `PUT /api/v1/plans/{id}`; status-only requests are no longer accepted. The history endpoint is authenticated and supports cursor paging. No plan operation makes NAS changes or buys hardware.

### Independent source coverage

Inventory, pools and native alerts are evaluated separately when an API returns a structured denial/unsupported error or an invalid result shape. Valid sources from that cycle are committed, while the failed source keeps its prior evidence and last-success timestamp. A denied inventory read does not mark drives absent; a denied pool read does not replace topology or advance healthy-snapshot counts; an invalid alert response does not mark existing alerts absent. Empty, valid arrays still mean that the source returned no entries. Duplicate pool GUIDs/alert IDs and malformed row shapes are rejected rather than treated as multiple observations or missing assets.

The NAS header's **Last successful source read** is the time of the latest committed inventory, pool, alert or optional cache read. It does not make the other capabilities fresh. Check their individual coverage cards and evidence timestamps. Skipped inventory is not counted as a successful read in that cycle. If none of the attempted monitoring sources returns valid data, the overall last-success time is retained and retry backoff increases. Retry delays never shorten the configured polling interval, including intervals longer than the normal 15-minute backoff ceiling. Full SMART/recovery coverage still remains explicitly unsupported.

Transport/session loss, cancellation and unexpected normalization/database failures continue to discard the uncommitted storage cycle. Successful API-method isolation does not claim independent recovery from every connection failure. Release and NAS identity checks remain prerequisites before any storage reads. No added NAS methods, permission changes, native alert acknowledgments or maintenance commands are involved.


### Import an existing SMART report

Storage → select a drive → Import SMART report accepts an existing smartctl JSON file or pasted JSON (maximum 1 MiB). Preview the readings, review warnings, then retype the selected serial to save. This workflow runs no command on the NAS. The report must match the selected drive's exact model and serial; ambiguous identities are rejected.

The importer supports JSON format 1.0 from smartctl 7.x for ATA, NVMe and SCSI. SCSI identity requires the curated scsi_model_name to exactly match the tracked model. Supported evidence is temperature, power-on hours, four recognized ATA counters (reallocated, pending, offline-uncorrectable and transport CRC), and NVMe media/data-integrity errors, critical-warning bitmask, available spare capacity, spare threshold, and percentage used. SCSI grown-defect list elements, curated power cycles and overall SMART assessment are also retained. SCSI uncorrected read/write/verify counters are supported; full SCSI log detail and self-test histories remain unsupported; SCSI protocol alone does not establish a SAS interface. Missing, unsupported or inexact counters remain unknown, never zero. Raw ATA counts and normalized scores are displayed separately. ATA raw counts require an explicit vendor-unit confirmation before health rules may use them. Command-error flags or missing report time leave numeric readings unverified. A report is not a complete health assessment or a predicted failure date.

A continuity label is optional. Leave it empty unless you can verify counter continuity; without it, imports cannot drive counter-delta comparisons or automatic resolution. Import uses the report timestamp, not upload time. Older or same-time readings remain in history but cannot change finding state when a later or same-time observed numeric reading already exists for that drive, metric and unit, including across report sources. This prevents old reports from reopening a resolved episode. Unknown or unverified readings do not establish that ordering boundary; a genuinely later adverse observation can create a new episode.

Saving commits the curated report, observations, finding changes and authored activity together. Equivalent curated reports cannot be imported twice by changing whitespace, continuity or confirmation. Saved report history shows the latest 50 reports. The original JSON and unrelated fields are not retained: keep the source file separately. Curated identifiers and supported readings remain in application backups.

The parser contract was checked against smartmontools 7.4 source: [ATA JSON output](https://github.com/smartmontools/smartmontools/blob/0c0bada5eedb759c9d7bb50ff54959cf0809c5a9/smartmontools/ataprint.cpp), [NVMe JSON output](https://github.com/smartmontools/smartmontools/blob/0c0bada5eedb759c9d7bb50ff54959cf0809c5a9/smartmontools/nvmeprint.cpp), and the [smartctl manual](https://manpages.debian.org/trixie/smartmontools/smartctl.8.en.html). Synthetic fixtures verify this implementation; real drive/vendor reports still need validation.


### Pool capacity planning

Storage now shows a capacity assessment for each stable pool GUID, with selectable 7-, 30- and 90-day windows. It reports the latest allocated/size bytes in the saved binary or decimal display units, measured daily allocation growth, and approximate days to an **80% allocated planning marker**. This is not an exhaustion guarantee or drive-failure prediction. Pool allocation is not dataset logical usage, and snapshots, compression, workload and deletions can change future growth.

The version-one policy fits a straight line through the last detailed reading on each UTC day. It requires seven sampled days spanning at least 80% of the selected window, a current latest reading (three polling intervals, minimum five minutes), consistent source schema, online state, known capacity and stable topology signatures. It withholds estimates after a layout/size change, a single allocation drop greater than 1% of capacity, a gap over two days, invalid or missing evidence, duplicate sample times, or a linear fit below 0.70. Flat/declining usage has no projected date; estimates beyond ten years are suppressed. R² is shown as a trend-fit measure, not a probability or confidence interval. Paused collection suppresses the projection. These explicit heuristic limits are not validated ZFS performance limits.

Signatures retain role, member GUID/type, grouping and non-disk layout names, excluding changing paths/status/error counters. Old snapshots have no invented signature and cannot support forecasts; a stable new evidence window must accumulate. Daily retention summaries preserve ranges but not the detailed topology proof, so short configured detail retention can make longer forecast windows unavailable. The read-only endpoint streams a bounded 300,001-row window with a connection/pool/time index; no new NAS calls were added. Real-host performance is still a pilot gate.

Capacity field semantics and topology roles follow the pinned [TrueNAS 24.10.2 pool entry contract](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/pool_/pool.py). Installed-patch payload validation and dataset-level history/forecasts remain outstanding; the optional latest dataset usage view is described below.


### UI accessibility checks

The browser owner workflow includes 18 axe scans using WCAG 2/2.1 A and AA tags across setup, overview, planning, inventory, recovery, settings, mobile storage and expanded editing/import dialogs. Full scan results are attached to the Playwright run, including items that require manual review. The workflow also checks keyboard skip navigation, disclosures, serial-confirmation gating, mobile navigation visibility and horizontal overflow. Run the normal frontend build and then `npx playwright test` from `src/Web` after building the backend.

Secondary text uses a stronger shared sage color, small labels are at least 11px, common input outlines and focus indicators are clearer, and reduced-motion preferences are honored. All seven mobile navigation destinations are visible in a two-row grid. Automated checks follow the [Playwright accessibility-testing guidance](https://playwright.dev/docs/accessibility-testing); they do not establish complete WCAG compliance. Screen-reader/user assessment, comprehensive keyboard coverage, and additional state/theme checks remain.


Display preferences apply immediately after a successful Settings save. Pool snapshots, daily ranges, capacity estimates and application backup sizes share a formatter using explicit binary (KiB/MiB/GiB/TiB) or decimal (kB/MB/GB/TB) labels; only presentation changes, never stored byte counts or forecast calculations. Null values remain Unknown, and small nonzero byte values are displayed in smaller units instead of rounding to zero GiB. Backup history, notification history, SMART report timestamps and plan-change history use the saved timezone. Editing an unsaved timezone draft does not affect existing dates. Explicit UTC/browser-timezone input fields and UTC daily grouping boundaries retain their stated semantics.


Storage view refreshes are bounded to 15 seconds and can be retried manually. A failed or malformed response preserves the last successfully loaded view with a visible warning. The view-loaded timestamp is distinct from each source's last successful collection time. Superseded and unmounted requests are aborted and cannot replace newer evidence. Pool-history requests follow the same timeout/response-order rules; changing history pages hides rows from the previous page until the requested page arrives, and Back to latest remains available during loading or failure. These are browser resilience measures and do not replace collector freshness checks or the live outage/pilot gates.


### Drive evidence freshness

Each drive now has a recorded-evidence summary for the twelve currently supported metrics. It separates the latest submission (receipt order) from the newest observed measurement (observation-time order). Uploading old evidence cannot renew its age. A later missing/unverified submission is visible alongside the earlier observed value rather than replacing it with zero or silently hiding the failed/missing report. The supported-metric catalog is not a claim that every metric applies to the selected protocol or that automatic SMART coverage exists.

Settings → Drive evidence review interval controls the existing local freshness policy: a measurement becomes stale after three intervals (default 90-minute interval, 270-minute freshness window). This is also the freshness window used by the existing two-later-zero media-resolution rule. This setting does not run or schedule NAS commands or self-tests. A fresh measurement is not a healthy-drive verdict; stale/missing evidence does not resolve existing findings. Each item exposes source/schema, report time, receipt time, author and missing reason where available. Summaries use indexed server-side grouping across retained observations rather than the chart's latest-1,000-reading display limit. Invalid legacy measurements remain unknown.


### Reproducible local container validation

With Docker running, build and exercise the production image:

```sh
docker build -f src/Api/Homelab.Api/Dockerfile -t homelab-control:mvp-validation .
python3 scripts/validate-container.py
```

Run as a non-root host user. The script creates private disposable deployment directories and two uniquely named Compose projects, uses random loopback ports and generated local certificates, validates HTTPS with the generated certificate (no TLS bypass), and removes its containers/networks/data afterward. It seeds one disabled synthetic NAS profile directly into the stopped disposable database; it never enrolls a live credential or contacts a NAS. The built validation image remains available locally.

Checks cover production authentication, non-root UID/GID, dropped capabilities, read-only secret mounts, no Docker socket, owner/plan persistence, application backup creation, encrypted Data Protection keys, cookie survival across restart, and restore into a new data directory with new certificates/key ring while retaining the original owner account and plan. It also verifies recovery revision conflicts and evidence, spare inventory, dataset unknown-versus-zero values, historical scan-error peaks, and configured temperature policy across restore. The synthetic NAS stays disabled, records no collection attempts, and has its unusable placeholder credential cleared by restore. A single fixture memory snapshot is printed for context; it is not a representative resource-budget measurement.

The Dockerfile defaults to HTTPS port 8443. The build context excludes deployment secrets, PFX/password files, backup directories, generated host-side web output and test artifacts. Frontend assets are generated in the image build. A successful same-host restore does **not** prove independent-host recovery or the seven-day pilot acceptance gate.


### NVMe warning and endurance evidence

Existing smartctl NVMe reports now retain `critical_warning` (integer bitmask 0–255), `available_spare` and `available_spare_threshold` (whole percentages 0–100), and `percentage_used` (0–255). Missing, malformed and reserved out-of-range values remain unknown; command-error or undated reports remain unverified. The importer, manual observation form, trends and freshness catalog use distinct protocol-specific units.

Nonzero warning masks create one warning episode. Known bits are described as device-reported spare/temperature/reliability/read-only/backup-memory conditions; unknown bits remain explicitly unrecognized. Reported media-read-only state is critical; other nonzero warnings warrant urgent review. Two later fresh zero masks can resolve the warning only with matching source, schema, unit and known continuity group. Missing evidence and resets cannot resolve it.

Percentage used is a manufacturer endurance estimate. It may exceed 100, and 255 represents values above 254%. At 100 or above, a versioned rule opens a planning review; this is neither a failure probability nor an automatic replacement instruction. Available spare refers to internal device spare capacity, not stocked replacement drives. The application does not turn these values into a health score or failure date.

Current report provenance uses NVMe schema v3 and ATA schema v2 after adding power-cycle evidence. Counter comparisons do not cross schema boundaries. Legacy hash checks cover NVMe v1/v2 and ATA v1 reports; reimport cannot add a second observation or silently upgrade original evidence. Import a genuinely new report to establish the new baseline.

Field names and warning flags were checked against pinned [smartmontools NVMe output](https://github.com/smartmontools/smartmontools/blob/0c0bada5eedb759c9d7bb50ff54959cf0809c5a9/smartmontools/nvmeprint.cpp); range/endurance semantics follow the [NVM Express Base Specification 2.0d SMART/Health log](https://nvmexpress.org/wp-content/uploads/NVM-Express-Base-Specification-2.0d-2024.01.11-Ratified.pdf). These remain imported observations; no new NAS commands or polling methods were added.


The production validator also records a backup outcome and restore exercise, revises their policy scope, verifies that old-form submissions receive a conflict, and records a synthetic spare. After isolated restore it verifies that the original evidence and spare quantity/cost remain intact while the revised policy still requires new evidence. These are synthetic application records; no backup/restore action is performed on a NAS.

### Multiple NAS connections

Each scheduled batch collects up to four NAS connections concurrently, using separate database contexts. Each NAS has at most one collection in flight; the next batch begins only after the current batch completes and the 15-second scheduler delay elapses. The existing 30-second per-connection deadline, polling cadence and failure backoff still apply. Database writes remain serialized and network waits do not hold the writer. Connections waiting for a slot are rechecked for deletion, pause and rescheduling before contact. Already-started reads retain the configuration-revision check before committing evidence. This bounds concurrent work; it does not guarantee exact polling deadlines when all four slots are occupied. Persistent authenticated sessions are reused and fixed passive events request bounded snapshot refreshes; representative multi-NAS load measurements remain outstanding.


### Power-cycle history

Existing-report imports now retain the curated `power_cycle_count` as `power_cycles` in cycles, using exact nonnegative integers up to 9,007,199,254,740,991. The mapping follows pinned smartmontools [ATA output](https://raw.githubusercontent.com/smartmontools/smartmontools/0c0bada5eedb759c9d7bb50ff54959cf0809c5a9/smartmontools/ataprint.cpp) and [NVMe output](https://raw.githubusercontent.com/smartmontools/smartmontools/0c0bada5eedb759c9d7bb50ff54959cf0809c5a9/smartmontools/nvmeprint.cpp). The importer does not guess from raw attribute IDs or substitute unsafe-shutdown counts. Missing, fractional, negative, string or oversized counts stay unknown; missing report time or command errors keep numeric evidence unverified. Power cycles are operating history, not a failure probability, remaining-life estimate or standalone replacement trigger. Manual observations, trends and freshness summaries support the same metric and unit. No NAS commands or tests are run.


### Pool redundancy context

Storage now shows conservative configured layout context from the existing curated `pool.query` snapshot. Complete, fresh topology is required, including all six roles. Simple disk groups, mirrors and explicit RAIDZ1/2/3 are supported; a generic RAIDZ type requires an unambiguous `raidz1-N`, `raidz2-N` or `raidz3-N` name. GUIDs must be present and unique, with simple disk leaves. Missing roles/states/identity, nested replacement trees and unsupported layouts (including dRAID) leave assessment unavailable. Paused or stale collection suppresses it.

Each vdev retains its own configured tolerance; groups are never summed. Data, special and dedup groups with zero configured redundancy are identified. Cache and spare roles add no data tolerance. A degraded pool/member shows native-review context without deriving a remaining-failure count. This is **not a safe-to-remove instruction**, a physical-drive mapping, shared-failure-domain analysis, or proof of recovery. Error counters and recovery evidence remain separate. Semantics were checked against [OpenZFS pool concepts](https://openzfs.github.io/openzfs-docs/man/master/7/zpoolconcepts.7.html) and the pinned [TrueNAS 24.10.2 topology transformation](https://raw.githubusercontent.com/truenas/middleware/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/pool_/topology.py); installed-patch fixtures remain a release gate.


### Overview scan reports

The overview displays the newest retained per-NAS/per-pool snapshot when its source state is SCANNING or PAUSED. Newer completed or missing scan data supersedes an older in-progress report; receipt/insertion order does not override observation time. Progress and scan errors remain source-reported, with missing or invalid values shown as unknown. No completion time is inferred, and this view cannot start, pause or stop an operation.

Recent labels require an enabled connection, current pool coverage, a matching current topology GUID and a snapshot within three expected pool intervals. Paused, denied, stale or removed-pool evidence remains visibly last-known. The browser rechecks the evidence deadline every ten seconds, including when overview refresh fails. An empty list means no in-progress scan is recorded in the latest retained snapshots, not proof that every pool was checked. Full scan-event correlation, source start/end times and reported ETA preservation remain separate work.

### Source scan timing (2026-11-01)

Pool snapshots now retain nullable NAS-reported start, end, pause and remaining-seconds fields with a nullable-column migration; existing history stays unknown rather than acquiring invented times. The parser accepts TrueNAS EJSON epoch-millisecond objects, rejects future/invalid dates and contradictory end/pause chronology, and keeps scan timing separate from fetch time. End/pause requires a valid start; estimates are retained only for active scans. Snapshot normalization uses schema v2.

Overview and pool history share keyboard-expandable source timing details. A reported pause is labelled explicitly even when the raw scan state remains SCANNING. Remaining time is an approximate value at the pool read, never a countdown or promised completion date. Collection remains read-only. Live installed-patch verification and scan-event correlation remain outstanding.

Contract references: [TrueNAS EJSON encoder](https://github.com/truenas/api_client/blob/master/truenas_api_client/ejson.py) and [py-libzfs scan properties](https://github.com/truenas/py-libzfs/blob/master/libzfs.pyx). These upstream references do not replace installed-version fixtures.

Validation: frontend production build, 310 backend tests, migration/model consistency check, and browser workflow with 18 accessibility scans passed. Expanded timing was checked with keyboard activation and a 390px mobile screenshot. The concurrency test now establishes both active reads before releasing the fast NAS, eliminating its scheduler-order assumption. No live NAS was contacted.

### Pool integrity status (2026-11-01)

Pool snapshot schema v3 retains a bounded status-code token independently of ONLINE/DEGRADED state, with an additive nullable migration that leaves older evidence unknown. Free-form status_detail is not persisted. The collector creates a separate critical pool-integrity-v1 finding for CORRUPT_DATA or CORRUPT_POOL, including pool GUID, source method, fetched timestamp, status and native/recovery review guidance. Repeated reports update the existing episode; later ONLINE/OK/missing/denied reports do not automatically resolve it or prove damaged data was recovered. A new adverse read after documented resolution creates a new episode. SUSPENDED and OFFLINE now also trigger the existing critical availability-state rule. No NAS methods were added.

Storage history displays state and status code separately with an explicit ONLINE/data-integrity limitation. Thirteen new backend cases cover corruption while ONLINE, deduplication, persistence across absent/OK/denied reads, recurrence, additional unavailable states, bounded code parsing, source-detail exclusion, cross-NAS isolation and avoiding false corruption claims from scan errors/other codes.

Sources: [release-specific pool contract](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/pool_/pool.py) and [upstream py-libzfs status mapping](https://github.com/truenas/py-libzfs/blob/master/libzfs.pyx). Installed-patch verification remains open. Full per-vdev error deltas, reboot/reset boundaries, physical membership mapping and scan-event correlation remain outstanding.

Integrity validation: final frontend production build, all 323 backend tests, EF model/migration consistency and the browser workflow with 18 accessibility scans passed. Browser assertions cover CORRUPT_DATA alongside ONLINE in pool history. No live NAS or real notification destination contacted; full-MVP operational gates remain open.

### Integrity evidence in daily retention (2026-11-01)

Daily pool summaries now preserve status codes and group known codes independently, so ONLINE/CORRUPT_DATA and ONLINE/OK readings cannot collapse into one summary. Unknown-code groups retain their legacy grouping keys for continuation across interrupted pre-upgrade batches. A nullable migration leaves existing daily summaries explicitly unknown; no historical code is inferred.

Older-history cards display the recorded status code and flag historical corruption with a recovery-review explanation. Reading counts remain counts of observations, not incidents. Three backend cases verify separation and merging across batches, legacy-key continuation without duplicate groups, and migration preserving existing daily measurements. Existing atomic rollback, backup-before-deletion and retention-boundary tests continue to apply. No collector methods or NAS permissions changed. Full scan-event preservation, counter/reset history and live/pilot requirements remain open.

Daily integrity validation: frontend production build, all 326 backend tests, migration consistency and the complete browser workflow pass. Nineteen accessibility scans now include expanded daily integrity history; browser checks verify the historical corruption explanation and mobile overflow. No live NAS was contacted. The full MVP goal remains active.

### Overall SMART report evidence (2026-11-01)

Existing-report imports now curate smart_status.passed as a nullable boolean with observed/unknown/unverified quality. Missing or nonboolean fields never imply success. Missing report time, command/device errors, and a passed result conflicting with exit bit 3 remain unverified. Preview and stored-report details label the result as a source observation at report time, separate from current health and self-test completion. Passed results do not modify numeric evidence or resolve findings; failed results include prompt recovery/native-review guidance without a failure deadline.

ATA parser schema advances to v3 and NVMe to v4. The source fingerprint includes the overall value, while a before-overall compatibility hash and all earlier hashes preserve duplicate rejection across upgrades. Optional curated fields keep earlier saved JSON readable without invented status. No database migration or NAS method changes. This increment preserves and presents overall status; the dedicated overall-status finding rule was subsequently added below; full self-test logs and remaining protocol coverage are not yet implemented.

Sixteen parser cases cover both protocols/booleans, invalid values, command/clock quality, legacy JSON and hash behavior. Real HTTP tests additionally verify duplicate rejection for the preceding parser, persisted overall result, and an adverse NVMe finding staying open despite a reported pass. Source contract: [smartmontools ATA output](https://github.com/smartmontools/smartmontools/blob/0c0bada5eedb759c9d7bb50ff54959cf0809c5a9/smartmontools/ataprint.cpp) and [NVMe output](https://github.com/smartmontools/smartmontools/blob/0c0bada5eedb759c9d7bb50ff54959cf0809c5a9/smartmontools/nvmeprint.cpp).

Overall SMART validation: production frontend build, 342 backend tests and the full browser workflow with 19 accessibility scans pass. Browser checks cover legacy unknown overall status and a reported failed result with explicit limitations; mobile preview inspected. No live NAS or additional device commands were used. Broader MVP gates remain open.

### Overall SMART failure action queue (2026-11-01)

Import commits now derive a separate smart.overall_failed observation from the reviewed overall assessment (flag 1 = reported failed; 0 = reported passed; missing = unknown). It participates in the same atomic import, source-time ordering and provenance as detailed metrics; report fingerprints are unchanged because the boolean was already fingerprinted. The observation catalog now includes thirteen metrics. Evidence freshness displays human-readable passed/failed labels rather than binary values.

The smart-overall-failed-v1 rule creates an urgent review finding only for observed failure evidence, with the report timestamp and recovery/native-review guidance. Missing/undated/unverified evidence does not create a finding. Later passes do not automatically resolve this rule. Existing cross-source ordering prevents older/same-time reports from reopening a reviewed episode after later observed evidence. Historical failures are labelled as report evidence, not predictions or present-tense verified device failures.

Eight new backend cases verify quality/validation, nonbinary rejection, pass persistence, manual resolution/backfill suppression and later recurrence. Actual authenticated HTTP report imports verify an initial failure creates the finding and two later passed reports leave it open while existing NVMe warning resolution behaves independently. No NAS commands or schema migrations added. Dedicated self-test logs, SAS coverage, other full-MVP features and live/pilot gates remain outstanding.

Overall failure workflow validation: final frontend build, all 350 backend tests and the full browser workflow with 19 accessibility scans pass. Browser fixtures verify the human-readable failed assessment alongside stale source evidence; mobile evidence cards inspected. No live NAS contacted. Full MVP remains in progress.

### Current production deployment validation (2026-11-01)

Rebuilt and exercised the production image after scan timing, pool integrity findings/history retention, overall SMART import/status/action-queue and notification retry changes. Validated Linux ARM64 image sha256:21c314d00017a51b29da9bb4fabe9c235e02a97ec436198e08b062a1c8c2b9c8.

Extended scripts/validate-container.py to verify referenced JavaScript/CSS are served successfully rather than receiving the SPA fallback, and to inspect the actual backup archive database read-only for SQLite integrity and the current scan/integrity columns and migration. Existing production HTTPS, Secure owner cookies, non-root/capability/secret isolation, recovery revision conflicts, spare inventory, encrypted key ring, cookie-preserving restart and isolated restore all pass. Restored owner, plan, recovery evidence and spare stock remain intact; revised recovery scope remains unverified as expected. No NAS enrolled or contacted, and both disposable Compose deployments were cleaned up.

The empty/synthetic fixture memory snapshot was 98.01 MiB, not representative steady-state measurement. This is a same-host container recovery exercise, not independent physical-host restore or the seven-day pilot. Latest backend verification remains 350 passing tests; frontend/browser verification remains 19 accessibility scans from the preceding implementation pass. The full MVP goal remains active with implementation and live-environment gates outstanding.

### SCSI grown-defect evidence (2026-11-01)

Added smartctl JSON SCSI imports using exact curated scsi_model_name/serial identity and the scsi_grown_defect_list element count. SCSI schema starts at v1; ATA/NVMe schemas and fingerprints are unchanged. Missing/invalid/noninteger/inexact counts remain unknown and command/date quality gates apply. No fallback to ATA attributes, NVMe media errors, generic model_name, or guessed vendor/product identity is used. SCSI protocol is not asserted to be SAS.

The scsi.grown_defects metric uses defects units in manual import, trends, report preview and the fourteen-metric freshness catalog. A strictly comparable increase produces an urgent review finding; first/stable/decreasing/unknown-continuity observations do not manufacture a new adverse trend or repair conclusion. The count is not relabelled as ATA sectors and does not predict failure time. Overall SMART failure handling remains independent.

Sixteen new parser/rule/validation cases plus real authenticated API import checks cover source identity, unknown fields, numeric bounds, quality, stable/reset/changed provenance, duplicate rejection, saved observations and one increase finding. Primary contract: [pinned smartmontools SCSI output](https://github.com/smartmontools/smartmontools/blob/0c0bada5eedb759c9d7bb50ff54959cf0809c5a9/smartmontools/scsiprint.cpp). No NAS methods or database migration added. Full SCSI read/write/verify/error logs, self-tests and live vendor validation remain outstanding.

SCSI validation: production frontend build, all 366 backend tests and the complete browser workflow pass. Twenty accessibility scans now include the SCSI preview; browser assertions verify defect units and the labelled metric. Mobile preview visually inspected. The last production container image predates this increment. No live NAS contacted; full-MVP and live/pilot gates remain open.

### SCSI uncorrected operation counters (2026-11-01)

SCSI report schema v2 now retains total_uncorrected_errors independently from read, write and verify sections of scsi_error_counter_log. Missing/invalid/fractional/oversized values remain unknown, and corrected counts/correction invocations never substitute. Manual entry and trends use errors units; report labels and the seventeen-metric evidence catalog preserve operation meanings. Comparable increases create separate urgent review findings without asserting affected files, failed components or failure dates. Stable/decreasing values or a changed continuity group do not produce an increase.

Added an optional SCSI-v1 fingerprint to prevent replay of reports imported before the additional counters were supported. ATA/NVMe fingerprints remain unchanged. Nine additional parser/validation/rule cases and authenticated API checks verify operation isolation, unknown-vs-zero, corrected-count exclusion, legacy replay rejection without writes, persistence, and separate grown-defect/uncorrected-read findings. No NAS read methods, device commands or database migrations added. Contract follows the pinned smartmontools scsiprint.cpp error-counter serializer referenced above. Full SCSI log detail, self-test records and live vendor fixtures remain outstanding.

SCSI counter validation: frontend production build, all 375 backend tests and the full browser workflow with 20 accessibility scans pass. Browser assertions distinguish read errors, zero write errors and unknown verify errors; mobile preview visually checked. No live NAS contacted. Full-MVP and live/pilot gates remain open.

### Evidence coverage usability (2026-11-01)

Drive evidence now leads with metrics that have submitted or retained observed evidence. An explicit count and keyboard-accessible disclosure expose metrics with no submissions, reducing the default wall of unsupported protocol cards without hiding submitted unknown/unverified readings. The view states that coverage is not a health score and includes an empty-evidence explanation. Results and failures are keyed to the selected disk, so an earlier drive's evidence cannot render beneath a newly selected identity while loading.

The SMART overall-status explanation now uses normal secondary text rather than the large numeric-reading style, preserving the hierarchy between the reported result and its limitations. Browser checks exercise missing-metric disclosure, expanded state, keyboard activation and retained stale/unverified evidence. No backend or NAS collection changes.

Evidence UX validation: final frontend and solution builds pass, along with the full browser workflow and 21 accessibility scans. Compact mobile coverage and the revised report typography were visually inspected. Backend code is unchanged; its latest suite remains 375 passing tests. Full MVP implementation and live/pilot gates remain open.

### Explicit local temperature review policy (2026-11-01)

Added an optional global Celsius review limit and required policy basis to operating settings. The additive migration leaves all existing installations disabled (null limit). The UI explicitly says this applies to new imported temperature evidence for all tracked drives, including historical reports; no automatic sensor collection or universal safe range is implied. The validation bounds are representability/physical input bounds, not recommended operating temperatures.

An observed Celsius reading strictly above the configured limit creates a local-temperature-policy-v1 review finding containing the measured value, observation time, applied limit and recorded basis. Existing chronology/provenance rules still apply. Missing/undated/unverified readings and the timestamp-free NAS cache cannot trigger the policy. Cooler readings or disabling/changing policy never erase an existing finding. Already stored observations are not re-evaluated when settings change.

Eleven new policy/validation/persistence cases cover default disabled, boundary, invalid numeric limits, required/bounded basis, quality and retained findings. Authenticated HTTP checks verify invalid settings rejection, policy persistence, temperature import producing a review with the basis, and disabling. No NAS methods or credentials changed. Per-model manufacturer specifications, broader automatic evidence coverage and remaining full-MVP/live gates remain open.

Temperature policy validation: frontend production build, all 386 backend tests, EF migration consistency and the full browser workflow pass. Twenty-two accessibility scans include the configured local policy. Browser checks verify default disabled, basis-required rejection without changing saved settings, save/disable persistence and mobile overflow; policy layout visually inspected. No live NAS contacted. Full-MVP/live/pilot gates remain open.

### Durable source-identified scan history (2026-11-01)

Added PoolScans records keyed uniquely by NAS connection, pool GUID, SCRUB/RESILVER function and source start time. Repeated reports update the latest reported state/timing/progress/errors without multiplying scans; older reports cannot overwrite newer state. Missing/invalid start times and unsupported functions/states do not create identities. Each group retains first/last fetch times independently of source start/end. Latest missing measurements stay unknown rather than borrowing an older value.

The migration backfills groups from identifiable retained pool snapshots using latest fetch/ID ordering and earliest fetch provenance, without deleting original snapshots or inventing missing timing. Collection updates are inside the existing transaction/writer lease. Groups survive detailed-snapshot compaction and are retained for two years after the last report; expiration uses bounded, atomic, backup-first history cleanup. Findings remain separate and are not resolved by scan completion.

Added authenticated connection/pool-scoped pagination and a keyboard-accessible Storage disclosure with explicit last-known semantics, source timing, unknown errors and manual refresh/retry. The grouping key is source evidence, not a guaranteed native operation ID; repeated starts with identical source timestamps cannot be distinguished. No current-state, recoverability or scan-success inference is made from FINISHED alone.

Ten new migration/collection/grouping/retention cases plus process-level API checks verify source identity, invalid/missing timing, deduplication, out-of-order reads, source-denied preservation, NAS/pool/function isolation, persisted UTC fields, retention boundaries, authentication, paging and invalid cursors. No new NAS methods or commands. Full topology/member/incident correlation and live/pilot/full-MVP gates remain outstanding.

Scan history validation: production frontend build, all 396 backend tests, migration/model consistency and the complete browser workflow pass. Twenty-three accessibility scans include expanded recorded scan history; browser checks verify finished state, reported errors and keyboard timing disclosure. Mobile scan history visually inspected. No live NAS contacted; the full MVP goal remains active.

### Scan-error peaks and review episodes (2026-11-01)

Source-identified scan groups now retain the highest known error count separately from the latest reported count. Zero, missing or lower later counts never erase that peak. Out-of-order reports may enrich the retained historical peak but cannot replace latest state or replay a finding. Migration backfills peaks from each stored group's latest count and matching retained snapshots, without creating notification episodes or guessing discarded history.

A positive current report creates one urgent scan-errors-v1 review episode per NAS/pool/scan identity. Repeated polls update that episode. After documented manual resolution, the same or lower count does not recreate it; a new retained peak or a different source-identified scan may create a new review. Zero/missing/FINISHED reports do not automatically resolve reviews. Guidance distinguishes scan errors from proven permanent data loss or failed-drive identification and requests native/recovery review, never a NAS write.

Four new lifecycle/bounds cases and expanded collector/migration checks cover retained peaks, unknown/current separation, out-of-order evidence, stable resolved episodes, higher-count/new-scan recurrence, invalid counts, migration without notification replay, and actual collection producing one review. Storage displays latest and peak counts with an explicit historical-error limitation. No new NAS methods or commands. Full topology/member/incident correlation and live-environment gates remain outstanding.

Scan-error validation: frontend production build, all 400 backend tests, migration consistency and complete browser workflow pass. Twenty-three accessibility scans remain passing; the scan-history fixture verifies a latest zero alongside an earlier peak and explicit recovery limitations. Mobile rendering visually inspected. No live NAS contacted; full-MVP/live/pilot gates remain open.

### Optional dataset usage

Settings → **Read dataset space usage** enables a bounded, fixed-argument read on each connection cycle. Storage → **View dataset usage** provides name filtering, batches of 20, saved fetch time, unknown values, and expandable quota details in your selected capacity units. This option is off by default. Failed/denied/oversized reads retain the previous snapshot; successful empty responses replace it without claiming an empty NAS. The view expires its freshness locally, even if refreshing the app fails.

The latest curated snapshot contains up to 256 visible filesystem/volume entries; sampled inventory history is also retained. Parent usage includes descendants and snapshot space: do not sum rows as pool capacity. Dataset names are not permanent identities. Manual rename reviews preserve evidence and can be reversed, but do not establish GUID-based continuity or join measurements automatically. Dataset forecasts remain outside the MVP. The reviewed 24.10.2 API filters internal datasets. Installed-patch and permission verification are required before enabling. See [the fixed read manifest](docs/NAS_READ_MANIFEST.md) for exact arguments and source references. No NAS mutation capability was added.

### Current packaged validation — dataset and scan evidence

The production Linux ARM64 image was rebuilt and validated after dataset usage, durable scan history/error peaks, temperature policy and SCSI evidence changes: `sha256:7fee5b1c9f35533a35ffdf48b7e2ac2e7f940be8ad65240b51148163eb3cd194` (`homelab-control:mvp-validation`, 346,480,767 bytes).

The same-host production exercise passed HTTPS/owner authentication, bundled assets, process and secret isolation, recovery revision conflicts, spare stock, consistent backup integrity/current migrations, encrypted key storage, authenticated restart, and isolated restore using new certificates/key storage. It now verifies actual saved dataset values (including null versus zero), a completed scan with latest errors zero and retained peak seven, and the local temperature policy before and after restore. A synthetic `.invalid` NAS profile is seeded offline with collection disabled; no collection attempts occur, and restore clears its unusable placeholder credential. Both disposable deployments were removed. The 106.9 MiB fixture memory snapshot is not a pilot/resource-budget result.

This refresh verifies packaging and same-host recovery. Installed-NAS compatibility, an independent physical-host restore and the representative seven-day pilot remain unverified; the full MVP goal remains active. Source-level verification from the preceding implementation pass remains 413 backend tests and 24 browser accessibility scans.

### Appearance preferences

Settings → **Appearance** offers **System**, **Light**, and **Dark** reading modes. System is the default and follows the device preference, including changes while the app is open. An explicit Light or Dark choice takes priority. Changes apply immediately, survive reload, and synchronize with other tabs for this application origin. The preference is browser-local; it does not change server settings or NAS behavior and is not included in application backups. If browser storage is unavailable, the selection still works for the current session and the UI explains that it could not be saved.

Shared semantic colors cover the shell, shadcn components, evidence/history cards, forms, charts, warnings, focus indicators and dialogs. The login/setup screen uses the same browser preference. Reduced-motion preferences remain respected.

### Connection edit conflicts and input limits

Connection configuration saves, pause/resume, and credential rotation require the `configurationRevision` returned by the connection list. Each accepted edit changes that revision; stale or omitted revisions return 409 before changes or audit events are committed. The editor keeps your draft on conflict and offers **Reload saved connection**, explicitly discarding unsaved fields and clearing any entered key. Review the newly loaded endpoint/trust/state before retrying. Identity-baseline review also requires the current revision, exact saved/new serials and independent verification evidence.

Initial enrollment and rotation share the same API-key validation: nonblank, at most 8,192 characters, no control characters, and no secret trimming. Names are limited to 200 characters without controls; endpoint addresses are bounded to 2,048 characters, use secure WebSocket `/websocket`, and reject whitespace/control characters or port zero. Tunnel hosts are bounded and validated separately from the logical TLS hostname. Rejected inputs are not saved and do not add success audit records. Connection list responses expose a revision, never the API key or encrypted credential. Accepted credential rotations record the signed-in owner.

### Identity review and connection-context freshness

Identity-baseline acceptance now requires `configurationRevision` in addition to `previousSerial`, `newSerial`, and `evidence`. A stale or omitted revision returns 409 without changing the baseline or resuming collection. Automatic identity/release pauses and first authenticated certificate enrollment change the revision, protecting against previously opened editors. The collector adopts its own newly enrolled certificate revision so it can finish that valid collection while still discarding reads superseded by user edits.

Changing the logical endpoint, tunnel host/port, public CA, or certificate pin clears the current reported identity/check time and marks automatic source coverage unavailable until authenticated collection succeeds. The saved serial baseline, historical observations, findings and last-success timestamps are retained; unavailable manual adapters and disabled optional reads stay explicit. Resume only after reviewing the updated connection; identity acceptance cannot reuse the previous endpoint’s reported serial.

Capacity forecasts additionally require current supported pool coverage and membership in the latest saved topology. Failed, denied, stale, future-dated or absent coverage—and removed/unknown pool membership—withhold the forward-looking planning date while preserving historical measurements. These checks complement the existing sample-window, topology, growth-fit and paused-collection rules.

### Manual drive maintenance context

Drive detail → **Maintenance context** shows operator-recorded work alongside the selected 24-hour, 7-day, 30-day or all-history trend window. **Record past work** captures inspection, cabling, firmware, relocation or other work already performed outside the app, with documented/estimated UTC time, work outcome, summary, evidence and exact serial/WWN confirmation. Missing or ambiguous identity must be resolved first. Work marked completed is not a drive-health verdict; records never issue NAS commands, change counter continuity, or resolve findings. Replacements remain in their separate old/new-drive history.

Records preserve immutable original details and the signed-in author/recording time. Mark an incorrect entry with a reason; it stays visible with correction author/time. Record corrected details separately. Repeated identical submissions with the same request ID return the original entry, while changed reuse conflicts. If a save times out, refresh history before creating another event; retrying the same unchanged form is safe. History is scoped to immutable drive ID, ordered by recording sequence, and paged in batches of 25. The chosen trend window filters work time, including estimated dates, rather than upload time. Absence of a record is not proof that no work occurred.

Maintenance history is retained with manual evidence, included in database backups, and included in inventory exports. Redacted exports omit summaries, evidence, confirmed serial/WWN, authors and correction text. Only local application records are written; no maintenance task is executed or scheduled on the NAS.

Spare compatibility can be reviewed from each drive review in Planning. Choose an inventory spare, record the capacity/interface/sector-format/physical-fit evidence, confirm the drive identity and set a review deadline within one year. These are manual assessments, not automatic hardware certification. Inventory changes, expired deadlines, ambiguous identity, absent/retired drives or zero recorded stock prevent a review from being presented as currently applicable compatible stock. A new review for the same drive/spare supersedes earlier evidence without deleting it. Reviews never reserve units, decrement stock, purchase hardware or change the NAS; the same stock may appear in several plans. Private inventory exports include this evidence; redacted exports omit identity, asset name, author and free-text evidence.


Saved SMART report history is available from the import dialog in pages of 50. **Older reports** retains the initial import-time cutoff, and **Refresh report history** starts a fresh view. Failed page requests preserve displayed evidence. Report observation times remain separate from import times. The history endpoint uses immutable server-assigned import timestamps and a stable ID tie-breaker; it does not rewrite or delete imported reports.

Imported SMART reports retain explicit firmware/revision and interface/standard strings as historical evidence. ATA reads `firmware_version` and `sata_version.string`; NVMe reads `firmware_version` and `nvme_version.string`; SCSI reads `scsi_revision` and `scsi_transport_protocol.name`. Fields are bounded, control characters rejected, and missing or malformed values stay unknown. Protocol labels alone are never used to infer SATA, SAS, connectors, physical compatibility or link speed. Older saved reports remain readable with unknown device details. Missing source time or command/device errors keep the details unverified. This does not query firmware availability or perform upgrades.

Field mappings were checked against the upstream smartmontools emitters: [ATA](https://github.com/smartmontools/smartmontools/blob/0a80d22062b624149f634174e422b6cd29df8c59/src/ataprint.cpp), [NVMe](https://github.com/smartmontools/smartmontools/blob/0a80d22062b624149f634174e422b6cd29df8c59/src/nvmeprint.cpp), and [SCSI](https://github.com/smartmontools/smartmontools/blob/0a80d22062b624149f634174e422b6cd29df8c59/src/scsiprint.cpp). Unsupported fields remain unknown rather than being inferred from a device path or generic protocol.


ATA report imports also retain standard and extended self-test log sections separately (up to 256 entries per section). Entries preserve type/status codes and source text, device lifetime hours, remaining percentage and first-error LBA when valid. A missing log, a reported empty log and unavailable entries are distinct. Results use the high nibble of the ATA status byte, because smartctl can emit `passed: true` for an in-progress or reserved status. The original flag remains inspectable. Contradictory completion evidence is labeled inconsistent. See the [upstream ATA self-test emitter](https://github.com/smartmontools/smartmontools/blob/0a80d22062b624149f634174e422b6cd29df8c59/src/ataprint.cpp#L2642).

These are historical report entries, not current test scheduling or independently dated events. Lifetime-hour counters can wrap; no calendar timestamp or cross-report event identity is invented. No tests are started, and successful entries never clear findings. Imported ATA failures now create a review finding during new imports; non-ATA self-test log coverage remains unimplemented.


New ATA imports with source time and no command/device error create an `ata-self-test-history-v1` review finding when failure evidence has not previously been retained. A fingerprint of type/status code, device lifetime hours and first-error LBA suppresses repeated evidence across standard/extended logs and later reports; it is not a physical test-event identifier. Identical details after counter wrap can therefore remain indistinguishable, and changed details do not prove a new failure. Missing time, unverified logs, completion, aborted/interrupted tests and in-progress states do not create failure findings. Successful reports never auto-resolve a failure finding. Older backfills cannot replace newer finding evidence or reopen a resolved episode reviewed from a newer report. Investigate the original imported report, current native details and recovery readiness before recording resolution. The rule applies during new imports; it does not retroactively flag previously saved reports.


Finding details now offer **Record resolution** after investigation. Enter supporting evidence and confirm review of current observations and independent recovery readiness. The form is bound to the exact finding snapshot opened; changed evidence or controls require closing, refreshing and reviewing again before saving. Resolution is audited with the authenticated owner, timestamp and explanation, clears local review/snooze controls, and remains visible in finding history. It does not dismiss a native alert, clear ZFS errors, resume collection, change hardware or prove overall health. Later qualifying evidence may create another finding. Already-resolved findings cannot be overwritten through this action.


The collector now leases a persistent authenticated WebSocket session per NAS. A continuous receive loop answers idle server heartbeats and notices disconnects; replies are correlated to one serialized read, and unsolicited frames are discarded rather than queued or treated as evidence. The exact read-method/argument allowlist still applies. Healthy sessions are reused only while the connection revision, endpoint, tunnel routing, trust settings and credential fingerprint match. Plaintext credentials are not stored in the session cache. Timeouts/transport loss discard the session; reconnect authenticates again and forces disk inventory reconciliation even before its normal interval is due. Subsequent healthy cycles return to configured inventory cadence. Existing per-cycle deadlines, failure backoff and atomic commit guards remain.

The scheduler closes cached sessions for paused/deleted/edited connections on its next sweep; already-started reads retain existing revision guards before committing. Pausing does not promise instant cancellation of an already-started read. Shutdown disposes cached transports. The first certificate-pin enrollment changes the profile revision and can cause one additional authenticated connection. Passive event subscriptions request snapshot reconciliation as described below. Idle heartbeats do not refresh any evidence timestamps or establish current NAS health.


After authenticated version and system-identity validation, the 24.10 adapter subscribes only to the broadcast collections `alert.list`, `pool.query` and `disk.query`. No wildcard, caller-selected collection or event-source parameters are accepted. `ready` confirms subscription; denied/unsupported responses and later `nosub` loss are tracked independently per collection. Subscriptions are attempted once per session and retried when a new session is established. Review permissions and pause/resume the connection to retry a denied subscription. Ordinary polling remains independent of subscription denial.

Added/changed/removed events coalesce into sequence counters, not stored payload queues. Events and idle disconnects may bring a healthy connection's next snapshot forward, at most once per 30 seconds and only on the normal scheduler sweep; existing failure backoff and pause state take precedence. Disk/pool events additionally request inventory reconciliation. Events arriving during a collection remain pending for another bounded reconciliation. Event fields never update disk presence, health, native-alert state or evidence timestamps directly; only validated read snapshots do. Coverage cards and collection history distinguish passive updates from periodic reads. No event-completeness guarantee is inferred from an accepted subscription.

Protocol behavior was checked against the pinned TrueNAS 24.10.2 [WebSocket dispatcher](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/main.py), [alert broadcasts](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/alert.py), [pool broadcasts](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/pool_/pool.py), and [disk broadcasts](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/disk_/sync.py). Installed-patch permissions and actual event delivery remain live validation gates.

### Historical ZFS member evidence

New pool snapshots retain a curated, bounded member tree from the existing read-only `pool.query` response: string GUIDs, parent relationships, source roles/state/aliases and read/write/checksum counters. The Storage history disclosure separates these ZFS identifiers from verified physical drive and bay identity. Missing or malformed counts remain unknown; previous snapshots without this field are explicitly unavailable. No additional NAS commands are introduced.

Backup-gated daily retention groups the same reported member/state/alias context and preserves each counter's minimum, maximum and known-reading count. Ranges are not deltas: counter reset/reboot continuity and physical partition mapping remain outstanding. Lower counters never establish repair. An additive nullable-column migration preserves existing records; use the application backup/restore process for rollback.

### Comparing saved member counters

Detailed pool history compares adjacent saved snapshots, including the predecessor across pagination. The comparison requires matching NAS serial, software version, connection revision and reported member/parent/role/state/alias context. It uses `system.info.uptime_seconds` captured within bounded local read intervals; the [pinned TrueNAS 24.10 implementation](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/system/info.py) computes `boottime` from wall time minus uptime, so exact boot-time equality is deliberately not used.

Differences are withheld after any counter decrease, an uptime discontinuity (outside the read interval plus a two-second timing allowance), inconsistent/overlapping timing, a gap over one hour, changed context, or missing source evidence. A decrease is not recovery. Supported comparisons show observed net differences only: clears and restarts between polls can be missed, and these values are not guaranteed new error counts or physical-drive failure predictions. Daily summaries retain ranges, not ordered deltas. The additional context is collected from the existing `system.info` call; no new NAS method or write action is introduced.

### Pool member counter review findings

The collector raises one open review episode per pool when a supported adjacent-snapshot comparison shows an increase in a read, write or checksum counter. Findings retain timestamps, member IDs and before/after counts for up to 20 increased members, with an explicit total and truncation notice for larger sets. Parent and child member counts are not summed. Newer increases update the open episode and retain its review state; each update also retains its evidence in local activity history. Replayed/older evidence cannot reopen a resolved episode. A later, newly observed increase can create a new episode after manual resolution. Stable, missing, decreased or incomparable counts never automatically resolve this concern.

The finding points to Storage and recommends reviewing native evidence and independent recovery readiness before deciding on hardware replacement. It has no physical-drive assignment until mapping is established and does not authorize NAS changes. Snapshot, finding and activity are committed in the existing collection transaction.

### Inferred member-to-drive links

The [pinned TrueNAS 24.10 topology adapter](https://github.com/truenas/middleware/blob/ab87b14d2e721ec6fe6f917be02090c7e98a52df/src/middlewared/middlewared/plugins/pool_/topology.py) supplies `path`, resolved `device` and resolved `disk` fields. For ONLINE disk leaves, the collector matches this explicit chain against disk inventory read in the same cycle and the reconciled physical identity. It never strips partition suffixes or substitutes an older alias. Missing inventory/chain/identity, offline members, duplicate aliases and conflicting matches remain unavailable or ambiguous. Compatible multipath aliases can identify the same reconciled WWN. Reads are sequential, so a successful match is labelled inferred, not atomic or physically verified.

The saved member card displays the historical model, serial/WWN, mapping explanation and a link to drive evidence. Daily compaction preserves this context, and later alias reuse does not rewrite older snapshots. Known changes in inferred drive identity stop counter comparisons. Missing drive mapping does not fabricate an identity or prevent comparison of otherwise matching ZFS-only evidence. Findings remain pool-level; an inferred link never verifies a bay or authorizes removing a drive. If mapping enrichment exceeds the bounded snapshot size, the original counter/tree evidence is retained without enrichment. No additional NAS methods are called.

### Shared hardware investigation

Each drive has a Shared hardware section for operator-recorded controller, enclosure and power relationships. Create the component in Inventory first, then confirm the drive serial/WWN, record evidence and set a review expiry within one year. A later record can end or correct a relationship; earlier evidence remains visible. Exact request retries are idempotent, asset revisions are checked, and the form's reviewed relationship version is frozen until explicitly reloaded. Concurrent changes return a conflict instead of silently overwriting newer evidence.

Only the latest unexpired relationship with an unchanged active asset and a present, non-retired, unambiguous drive contributes to shared-component context. The panel shows other linked drives and their open findings, including evidence times. These findings may describe different times or older evidence: neither a common component nor correlated symptoms establish cause. No listed finding is not proof of health. Results are limited to 32 peers and 160 findings with an explicit truncation notice; relationship history is paginated. Inventory export includes relationship history and redacts operator evidence/identifiers in redacted exports. No NAS operation is issued.

The additive `DriveHardwarePaths` migration retains this history in the application database and backup. These manual records do not discover hardware paths, verify physical bays or authorize removing a disk.

### Reversible identity history groups

Drive details include an explicit identity-history review. Two distinct records from the same NAS can be grouped only after reviewing all affected group members, confirming both exact identities and asserting with evidence that they refer to the same physical drive. Review tokens bind the member identities, presence, bay/verification context and active merge links; changed context requires a new review. Groups are limited to 64 records and merges cannot create cycles. Full submission hashes make exact retries idempotent while changed submissions conflict.

The combined timeline labels observations and findings with their original disk IDs. Original records, counters, annotations and ownership are never rewritten, and counter rates remain separate. The split review previews the resulting groups and records the reason and actor when reversing a merge. Both the original merge and reversal remain in the audit trail and private inventory export; redacted exports omit their operator evidence. The additive `ReversibleIdentityHistory` migration retains these decisions in the application database.

These groups support reviewed historical reconciliation. They do not override ongoing discovery conflicts, change reported identifiers, verify a bay, or split previously mixed observations within a single original record. Those cases still need explicit identity investigation; no physical action is inferred from a merge.

### Reviewed discovery assignments

The NAS Storage card exposes a discovery-identity review using the latest bounded `disk.query` identity projection. Approvals require a running connection, inventory from the current connection configuration within 24 hours, a stable WWN with compatible serial/model evidence, an exact WWN confirmation and independent physical-identity evidence. Missing WWNs, contradictory duplicate WWNs and aliases claimed by different identities are not overrideable. The operator selects a matching existing original record; a new separate record is permitted only when no matching non-retired record exists. Existing identifiers and history are never overwritten.

Approvals are bound to normalized WWN, serial, model and connection revision, expire within a year, and apply only when future inventory still matches. Compatible multipath alias changes do not change the physical identity. A matching approval can resolve that WWN's identity-review finding; unrelated identity conflicts remain open. Revocation is audited and immediately marks the target identity as needing review; it never deletes the record. Expiry/configuration changes stop application of the approval. Bay verification, counter history, metadata and other findings are independent.

The `StableDiscoveryAssignments` migration stores decisions plus a bounded latest source projection. The UI supports source reload with renewed identity confirmation, lists all unexpired assignments and the latest 50 historical decisions, and records revocation reasons. Private inventory export includes decisions; redacted export removes WWNs, serials and operator evidence. Collection still uses only its existing approved read methods.

### Resilver context and unresolved drive evidence

When a pool read reports an active, unpaused resilver with a known start time, Homelab Control correlates ONLINE data/special/dedup leaves with unresolved adverse drive findings only when the same collection cycle supplies an unambiguous inferred identity mapping. It opens an urgent review (or preserves critical urgency) with the scan read time, original finding ID/rule, original observation time and retained evidence. Historical drive evidence may predate the scan; correlation does not establish simultaneous deterioration, identify the replacement target, or predict resilver success. Cache/spare roles, missing or ambiguous mappings, age/warranty reminders and endurance-only reminders do not qualify.

There is one correlation per NAS/pool/scan-start/source-finding episode. Repeated polls do not refresh the drive evidence's age or reopen a manually resolved correlation. A new scan or new source finding can warrant another review. Completion, source-finding resolution, missing telemetry and stable counters do not automatically resolve the historical correlation; review it locally after checking native evidence and independent recovery readiness. The finding links to pool and drive evidence. This adds no NAS methods and cannot interrupt a scan or alter a drive.

### Browsing drive observation history

The trend view uses `/api/v1/disks/{id}/observation-history` with the selected metric and 24-hour, 7-day, 30-day or all-history window. Each page contains up to 100 records. The initial observation-ID ceiling and UTC window endpoint remain fixed while paging, so later imports do not shift the results. Refresh history starts a new snapshot. Background dashboard refreshes and unrelated drive-note edits preserve the current pages; a successful observation or SMART-report import reloads history. Window filtering uses observation time; undated evidence is retained separately and never plotted. Records load in import order and are plotted by observation time, so a backdated import may appear on an earlier page.

The chart explicitly covers loaded records only. Load more to reach older retained imports; after 1,000 loaded records the next page replaces the displayed batch to bound browser work. There is no silent 1,000-record history limit in this view. Daily aggregates remain separate from detailed retained evidence. Negative Celsius readings have a negative chart baseline. The legacy `/observations` GET remains a bounded latest-1,000 array for compatibility; use the paged endpoint for history browsing.

### Observation change evidence

Each dated trend reading has a Change evidence disclosure. Comparisons use the chronological predecessor within the same disk/metric history snapshot, including predecessors outside the visible page or time window. Import order is not observation order. A matching source, schema, unit and explicit continuity group are required; both readings must be valid observed values. Duplicate observation times, missing/unverified predecessor readings and changed context suppress a numeric comparison. The app does not skip an intervening unknown reading to manufacture continuity.

Counter decreases are shown as a possible reset/discontinuity, never negative error rates or proof of repair. Temperature and spare-capacity gauges can show signed changes; percentage gauges use percentage points. Bitmasks and overall assessment flags are not subtracted. An earliest retained reading is a baseline with no invented zero. A displayed difference is an observed net difference across the stated times, not continuous coverage, a guaranteed count of new errors, a rate or a failure prediction. The operator's continuity label cannot prove that no reset occurred between samples. These comparisons are read-only presentation evidence; finding rules and their resolution behavior are unchanged.

### Recovery evidence integrity on upgrade

The `RecoveryEvidenceIntegrity` migration adds restrictive database links from backup outcomes and restore exercises to their protection policies. Existing evidence is preserved; deleting a policy with evidence or saving evidence against an absent policy is rejected by SQLite as well as the API. The migration checks for legacy orphan records before SQLite rebuilds the tables. If it fails with `RecoveryEvidenceRequiresExistingPolicy`, it leaves those records for review instead of discarding them or assigning them to an unrelated policy.

Keep the pre-migration backup and stop the service before investigating that failure. Inspect the affected record and policy IDs in a copy of the database. Recover the original policy from a known consistent backup, or restore a consistent application backup using the documented procedure. Do not manufacture a replacement policy merely to pass the constraint: scope, revision, destination and recovery evidence must remain truthful. After a verified repair, retry startup. Migration tests exercise valid upgrades, rejected orphan upgrades, retained evidence and a successful retry after restoring the original parent ID.

A historical bay confirmation does not override a currently ambiguous identity. Drive list/detail labels require identity review in that case, and outbound notifications withhold the bay label. The original bay confirmation and evidence stay on the record for audit; resolving identity ambiguity does not constitute a new physical-label check.

Recovery policies show three recent backup records and restore exercises in the runbook disclosure. Choose **Browse all recovery evidence** for pages of 50 retained records, with separate backup and restore views. Older policy revisions are labeled because the current policy scope may differ. Paging fixes the submission-time boundary; **Back to latest evidence** refreshes it. Historical records do not establish current recovery readiness.

The Activity page browses all retained audit events in pages of 50, ordered by recording sequence. New activity is included only when you choose **Refresh latest activity**; older-page requests keep a fixed record boundary. Each event shows its actor and timestamp.

Notification attempt integrity: upgrades refuse delivery-attempt records whose original delivery is missing, rather than deleting or reassigning them. If `NotificationAttemptRequiresDelivery` blocks an upgrade, keep the service stopped, preserve the pre-upgrade database/backup, and investigate recovery of the original delivery record. Do not create a substitute delivery simply to satisfy the constraint. Existing deliveries with retained attempts cannot be deleted.

Collection source integrity: capability, native-alert, collection-run and daily collection-summary records now require their original NAS connection. If `CollectionEvidenceRequiresNas` stops an upgrade, preserve the database and pre-upgrade backup, keep the service stopped, and investigate recovery of the original NAS record. The migration does not discard evidence or guess a different NAS. A connection referenced by these records cannot be deleted.

Drive-linked records: plans, aliases and annotations require an existing drive when they carry a drive reference; general plans may remain unlinked. `DriveRecordRequiresDisk` indicates a legacy dangling reference. Preserve the database and pre-upgrade backup and recover the original drive evidence before retrying; never reassign historical records or create a substitute identity merely to pass the upgrade. Referenced drives cannot be deleted.

Core identity integrity: disks require an existing NAS; observations and findings require an existing NAS and, when a drive is linked, the same NAS as that drive. NAS-level evidence may have no drive. `CoreEvidenceRequiresMatchingIdentity` stops upgrades containing missing or conflicting references before any table rebuild. Preserve the original database and backup and investigate the source evidence; do not silently move history to a different NAS or manufacture parents.

Reviewed identity and delivery integrity: discovery assignments and both sides of a history merge must belong to their recorded NAS. Each notification delivery must retain its original finding; channel tests are audit events, not finding-less outbox rows. Upgrade errors `ReviewedIdentityRequiresSameNas` or `DeliveryRequiresFinding` preserve inconsistent historical rows for investigation. Keep the original database/backup and recover original references rather than reassigning or deleting evidence to bypass the checks.

Dataset inventory history is sampled at the first successful read per UTC hour and connection revision, with source context and unknown values preserved. It follows detailed-history retention with backup-gated expiry. These samples do not identify renames or prove continuity across path reuse; the current adapter exposes names rather than stable dataset GUIDs. Use **Browse sampled dataset history** below dataset usage to choose retained inventories, filter names, inspect source context and page older samples. To record a reviewed rename, choose an earlier inventory, browse to a later one, then select **Review rename to this inventory**. Confirm both exact names and independent evidence. Rename history provides an audited reversal action.

Dataset rename reviews: a review links two retained inventories from the same known NAS identity, with chronological, distinct non-coexisting names of matching type/pool, exact confirmations and independent evidence. It records an operator assertion, not a discovered GUID, and never renames a NAS dataset or joins measurements automatically. Exact retries are idempotent; conflicting submissions are rejected. Reversal retains the original record, author/time and reason. Both inventories remain retained even after reversal so its evidence cannot disappear during expiry. Private exports include reviews; redacted exports omit names, notes and authors.

## Drive installation and purchase evidence

Each drive detail has an **Installation & purchase evidence** card. Record an optional installation date, estimated/documented confidence, and the supporting log or estimate basis. Record the original purchase cost separately from replacement budgets, with its three-letter currency, confidence and receipt/reference. Unknown costs are blank; zero explicitly means free. Purchase-date and warranty fields remain in Drive notes. Purchase evidence can be recorded even when the amount is unknown. Dates and costs do not create health findings or predict remaining life.

The authenticated `/api/v1/disks/{id}/acquisition` endpoint returns `{ "record": null }` until notes exist. `PUT` requires the returned revision (or null for the first record), installation date/confidence/evidence and purchase cost/currency/confidence/evidence. Conflicting revisions return 409 without writes. The form retains failed drafts; **Reload saved notes & discard draft** explicitly reloads current values. Saves record the signed-in author/time and retain before/after values in the local drive annotation audit. No NAS method is called. Existing installations migrate without invented dates or prices.

Private inventory exports include these records; redacted exports omit installation/purchase evidence and author. Numeric cost, currency and confidence remain in the redacted inventory, just like planning costs. Use secure references rather than payment details or secrets. Application backups preserve the record and its audit history.

## Installed-environment release evidence

Copy [the release evidence template](docs/release-evidence.template.json) to a private location and fill it during validation. Every gate starts unverified; fields left empty are unknown, not passing results. Record the exact application image and environment alongside dated evidence. The repository's automated results prove synthetic behavior only.

1. Confirm the installed TrueNAS patch, its API contract and actual credential permissions against [the fixed read manifest](docs/NAS_READ_MANIFEST.md). Review sanitized payloads before enabling optional reads. Preserve explicit gaps; do not broaden privileges to make a status green.
2. Record the control host's storage, power and network dependencies. Direct and externally tunneled connections use the same read-only API contract. Running locally on the NAS is possible through that API, but does not satisfy independent operation during loss of the NAS; the checker and recovery copy must remain elsewhere.
3. Use an operator-controlled connection interruption to verify stale source coverage, retained last-known data, unresolved findings and full refresh after reconnection. Do not stop NAS workloads or induce disk failures. Inspect runbooks/inventory while the NAS connection is unavailable.
4. Configure and test the chosen receiver in Settings, review identifier masking, then record actual delivery/deduplication and timing evidence. Separately verify the independent checker's alert and recovery delivery when the control service stops and restarts.
5. Restore an existing consistent application archive onto a clean, separate physical host using the procedure above. Preserve the original deployment. Record the archive's represented time, simulated loss time, start time and the time the restored application becomes usable. RPO is the data-age gap at simulated loss; RTO is elapsed recovery time. Compare retained evidence and runbooks before securely re-enrolling credentials. Proposed targets are 24-hour RPO and four-hour RTO.
6. Run a representative seven-day pilot. Record the reference host, drive/service counts, polling/retention settings, measurement definitions and intervals, memory, telemetry storage growth, collector traffic, NAS load impact and gaps. Verify retention and outage recovery. Targets are under 512 MiB steady-state memory and 5 GiB telemetry storage for 32 drives/20 services, or an explicitly reviewed revision. A smaller fixture, one memory sample or same-host restore cannot close these gates.

No release approval is inferred from this template. Keep it private, use evidence references instead of credentials or private payloads, and record unresolved checks in its final decision.

### Telegram notifications

In **Settings → Notifications**, select **Telegram**. Create a bot with [BotFather](https://t.me/BotFather), send the bot a message (or add it to the target group), and enter the bot token and chat ID. If you already use a Telegram bot for Servarr, the same outbound bot/chat pair can be used here. Save, send a test, then enable delivery. No incoming bot webhook or NAS setting is needed.

The built-in channel sends only to Telegram's fixed HTTPS `sendMessage` endpoint; no webhook-host environment setting is required. Token and chat ID are encrypted together and are not returned by the settings API. Re-enter both to change either. A destination change disables delivery until a new test succeeds. Revoking a bot token in BotFather requires saving and testing its replacement here. Restore clears saved notification credentials and disables delivery.

Messages are bounded plain text with link previews disabled. Identifier masking, critical maintenance bypass, the durable outbox, bounded retries and attempt history are shared with HTTPS webhooks. A successful Telegram response must explicitly confirm acceptance; acceptance does not mean the recipient read the message. An interrupted send can produce duplicates when retried, since Telegram does not accept the application's event ID as an idempotency key. Live delivery remains unverified until the owner configures and tests a real bot/chat. Protocol reference: [Telegram Bot API](https://core.telegram.org/bots/api#sendmessage).

### Local pilot measurements

`scripts/sample-pilot.py` appends private (0600 on creation) JSONL measurements without contacting the NAS or reading credentials/serials. Select the **Homelab API process ID**, not Aspire or the browser. Run a one-sample smoke check first:

```sh
python3 scripts/sample-pilot.py --database /path/to/homelab.db --pid API_PID --output /path/to/pilot.jsonl
```

For a seven-day foreground collection at five-minute intervals, add `--samples 2017 --interval-seconds 300`. Keep the selected process and sampler running; interruption stops sampling. Restart with a new output file and the new API PID after an application restart, retaining previous files as separate segments. The sampler rejects re-used process identities within a run and records unavailable RSS as null. Timestamps expose measurement gaps; it does not reconstruct missed samples or automatically approve a pilot.

RSS is for that API process only. Database/WAL sizes are file lengths, not allocated blocks or telemetry-only storage. Coverage fields are persisted capability statuses and last-success timestamps; compare them to expected cadence rather than assuming a stored supported status is currently fresh. Drive/service counts describe the installed workload. Collector network bytes and NAS load impact remain unknown and require separate evidence. Avoid interpreting total host traffic as collector traffic or a one-sample memory result as steady-state usage. `python3 scripts/test-sample-pilot.py` verifies read-only behavior, missing sources, append/output safety and process reuse handling.

Summarize one or more retained pilot segments with `python3 scripts/summarize-pilot.py /path/to/pilot.jsonl`. The report exposes malformed records, duplicate timestamps, missing measurements, workload mismatches and gaps larger than 1.5 times the requested cadence. The pilot report requires a cadence no slower than five minutes. A seven-day span with gaps does not pass local coverage; even complete local coverage never automatically approves release. Sampled peaks and net storage change are not continuous maxima or growth forecasts. Run `python3 scripts/test-summarize-pilot.py` for the report's synthetic checks.

On macOS, `sample-nas-traffic.py` can separately observe the API process's existing TCP socket counters for an exact connected NAS IPv4 address and port. It runs `nettop` locally, disables name resolution, opens no network connection, and writes only hashed socket identities and byte counters. Example: `python3 scripts/sample-nas-traffic.py --pid API_PID --nas-address NAS_IPV4 --port 443 --output /path/to/new-traffic.jsonl --samples 2017 --interval-seconds 300`. The output must be new; it is created with mode 0600. For an external tunnel, select its actual TCP destination; do not assume the logical TLS hostname is the network destination.

Counters describe currently observed sockets. A vanished socket is not zero traffic; closed connections between samples can be missed. Treat new socket identities, counter decreases and missing samples as continuity boundaries rather than summing cumulative counters. NAS load is not measured. This optional macOS evidence complements the portable local sampler; it does not on its own prove complete collector traffic or approve a pilot. Four parser/process-identity tests run with `python3 scripts/test-sample-nas-traffic.py`.

## Repository layout

- `src/Web`: React/TypeScript application, browser tests, package manifest and frontend Dockerfile; generated output in `dist`.
- `src/Api/Homelab.sln`: .NET solution, alongside `Homelab.Api`, `Homelab.AppHost`, `Homelab.ServiceDefaults` and `Tests/Homelab.Tests`.
- `src/Api/Homelab.Api/Dockerfile`: combined frontend/API deployment image, built from the repository root.
- Root `scripts`, `docs`, Compose and npm commands support development and deployment.

From the root, run `npm run install:web`, then `npm run dev:aspire`. Use `npm run build`, `npm test`, `npm run test:web` and `npm run lint` for verification. npm scripts are defined in `package.json`.

Build the application image with `docker build -f src/Api/Homelab.Api/Dockerfile -t homelab-control:mvp-validation .`. The frontend Dockerfile can independently export static assets: `docker build -f src/Web/Dockerfile --output type=local,dest=/tmp/homelab-web-assets .`; the combined API image serves the UI and authenticated API together.

### Stable development addresses

`npm run dev:aspire` explicitly selects the HTTPS AppHost profile. Default addresses are UI `http://localhost:26264`, Aspire dashboard `https://localhost:17247` (HTTP listener `http://localhost:15037`), API HTTP `http://localhost:5253` and API HTTPS `https://localhost:7213`. The UI port can be overridden with `Web__Port`. These exposed ports remain fixed across starts; a conflicting listener must be stopped rather than expecting an alternative port. Aspire can assign different internal process ports, and the dashboard login token can change between runs. Use the dashboard URL printed during startup to authenticate.

### Complete storage inventory CSV

Storage’s **Export CSV** button downloads all retained storage inventory and storage evidence for every NAS server, with stable IDs, exact record values, schema metadata and integrity checks. [Format and offline restore instructions](docs/STORAGE_CSV_EXPORT.md) describe restoring the CSV into a new application data directory. Integration credentials and owner accounts are excluded, and restored collectors start disabled. NAS pool configuration and files are outside this application-inventory export.
