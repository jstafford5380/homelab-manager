# Homelab Coolify Deployment Configuration

**Last updated:** November 2, 2026  
**Platform:** TrueNAS SCALE  
**Coolify version:** 4.6.0  
**Purpose:** Self-hosted CI/CD for applications deployed from GitHub to TrueNAS.

## 1. Architecture

The deployment platform uses the following components:

| Component | Responsibility |
|---|---|
| TrueNAS SCALE | Docker host and persistent storage |
| Coolify | Application builds, deployments, and GitHub integration |
| Dockerge | Management of existing homelab Docker stacks |
| Nginx Proxy Manager | Reverse proxy, local domains, and TLS termination |
| Cloudflare Tunnel | Public endpoint for GitHub webhooks |
| GitHub App | Repository integration and deployment triggers |

### Deployment flow

```text
GitHub
  │
  │ Push / webhook
  ▼
Cloudflare Tunnel
  │
  ▼
Nginx Proxy Manager
  │
  │ /webhooks/source/github/events
  ▼
Coolify
  │
  │ SSH (root)
  ▼
TrueNAS Docker Engine
  │
  ├── Build application
  ├── Create container
  └── Deploy application
```

GitHub webhooks are accessible publicly through Cloudflare Tunnel. The Coolify administration interface is accessed locally through Nginx Proxy Manager.

## 2. Coolify Installation

Coolify is installed as a TrueNAS Community App.

### Containers

| Container | Purpose |
|---|---|
| `ix-coolify-coolify-1` | Main Coolify application |
| `ix-coolify-postgres-1` | PostgreSQL database |
| `ix-coolify-redis-1` | Redis |
| `ix-coolify-realtime-1` | Soketi and legacy terminal services |
| `coolify-sentinel` | Coolify management component |
| `coolify-terminal-test` | Custom terminal WebSocket proxy |

### Container images

```text
ghcr.io/coollabsio/coolify:4.6.0
ghcr.io/coollabsio/coolify-realtime:1.0.20
```

### Relevant ports

| Host port | Destination | Purpose |
|---|---|---|
| 30275 | Main Coolify :8080 | Web administration |
| 6001 | Realtime :6001 | Realtime WebSockets |
| 6002 | Custom Nginx proxy :80 | Terminal WebSockets |
| 16003 | Realtime :6002 | Legacy terminal endpoint |
| 80 | Nginx Proxy Manager | HTTP |
| 443 | Nginx Proxy Manager | HTTPS |
| 8081 | Nginx Proxy Manager | Administration |

## 3. Local Server Configuration

The TrueNAS Community App initially created an invalid local server configuration.

### Original issue

The `servers` table contained:

```text
id: 0
name: localhost
ip: host.docker.internal
private_key_id: 0
```

However, the `private_keys` table was empty.

This caused:

```text
No query results for model [App\Models\PrivateKey] 0
```

### Resolution

1. Generated an ED25519 SSH key through Coolify.
2. Added the public key to the TrueNAS root account's authorized SSH keys.
3. Updated the Coolify server configuration to reference the generated private key.

The database correction was:

```sql
UPDATE servers
SET private_key_id = 1
WHERE id = 0
  AND private_key_id = 0;
```

**Important:** Key ID `1` was specific to this installation. Do not assume the same ID after reinstalling.

### Server connection

| Setting | Value |
|---|---|
| Server | localhost |
| Address | `host.docker.internal` |
| SSH port | 22 |
| SSH user | root |
| Authentication | ED25519 private key |

`host.docker.internal` resolved to `172.16.0.1` during troubleshooting.

Validated functionality:

```bash
whoami
hostname
docker info --format '{{.ServerVersion}}'
docker ps
```

The Coolify terminal successfully connected to TrueNAS as root and accessed Docker Engine 27.1.1.

### Security consideration

Coolify has root-level access to the TrueNAS host through SSH.

This grants significant control over Docker and the host environment. Protect Coolify administrator credentials and SSH private keys accordingly.

## 4. Docker Network

Coolify deployments initially failed because the Docker network named `coolify` did not exist.

The TrueNAS application created its own internal network:

```text
ix-internal-coolify-coolify-net
```

### Resolution

Manually created the network expected by Coolify:

```bash
sudo docker network create --driver bridge coolify
```

### Verification

```bash
docker network inspect coolify
```

**Important:** The manually created `coolify` network must be preserved. If Docker networking is reset or the network is removed, deployments may fail again.

## 5. Terminal WebSocket Workaround

### Problem

Coolify's terminal initially failed to connect.

The TrueNAS Community App runs terminal implementations in both its main application and realtime containers.

The implementations differ:

| Component | Main Coolify | Realtime |
|---|---|---|
| Terminal server | Newer implementation | Older implementation |
| Terminal utility files | Newer implementation | Older implementation |
| Internal port | 6002 | 6002 |

The realtime container originally published host port 6002.

Although the browser established a WebSocket connection, terminal session initialization failed.

### Resolution

A custom Nginx container was deployed through Dockerge to forward terminal WebSocket traffic to the main Coolify container.

The TrueNAS realtime backend host port was changed from `6002` to `16003`, allowing the custom proxy to use host port `6002`.

### Dockerge stack

**Stack name:** `coolify-terminal-test`

**Storage location:**

```text
/mnt/.ix-apps/app_mounts/dockge/stacks/coolify-terminal-test/
```

**compose.yaml:**

```yaml
services:
  terminal-proxy:
    image: nginx:alpine
    container_name: coolify-terminal-test
    restart: unless-stopped

    ports:
      - "6002:80"

    networks:
      - coolify-network

    volumes:
      - ./nginx.conf:/etc/nginx/conf.d/default.conf:ro

networks:
  coolify-network:
    external: true
    name: ix-internal-coolify-coolify-net
```

**nginx.conf:**

```nginx
server {
    listen 80;

    location / {
        proxy_pass http://ix-coolify-coolify-1:6002;

        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;

        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}
```

### Validation

The terminal endpoint was tested with:

```bash
curl -i --max-time 5 \
  -H "Connection: Upgrade" \
  -H "Upgrade: websocket" \
  -H "Sec-WebSocket-Version: 13" \
  -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" \
  http://127.0.0.1:6002/terminal/ws
```

A successful WebSocket upgrade returns:

```text
HTTP/1.1 101 Switching Protocols
```

An unauthenticated test may subsequently return:

```text
Unauthorized: Missing required tokens
```

This is expected without a valid Coolify terminal token.

### Maintenance considerations

This workaround depends on:

- The main Coolify container exposing its terminal service internally on port 6002.
- The shared TrueNAS Docker network.
- The main container's Docker network alias.
- The Dockerge stack remaining active.

If Coolify or the TrueNAS Community App changes its container topology, update this configuration.

## 6. Nginx Proxy Manager

Nginx Proxy Manager is the primary reverse proxy for the homelab.

Coolify's built-in Traefik proxy is not running.

### Coolify administration

**Hostname:**

```text
https://coolify.provausio.com
```

**NPM configuration:**

| Setting | Value |
|---|---|
| Domain | `coolify.provausio.com` |
| Scheme | HTTP |
| Forward host | `192.168.1.181` |
| Forward port | 30275 |
| WebSocket support | Enabled |
| SSL certificate | `*.provausio.com` |
| TLS termination | Nginx Proxy Manager |

**Important:** The upstream scheme must be HTTP.

Using HTTPS to reach Coolify's port 30275 caused HTTP 502 errors.

### TLS certificate

A Let's Encrypt wildcard certificate was issued for:

```text
*.provausio.com
```

The certificate is managed through Nginx Proxy Manager using DNS challenge validation.

The wildcard covers single-level subdomains, including:

```text
coolify.provausio.com
```

It does not automatically cover:

```text
provausio.com
app.dev.provausio.com
```

## 7. Realtime WebSocket Routing

### Problem

Coolify displayed a realtime service warning even though its UI, deployment triggers, and terminal were functional.

The browser attempted to establish:

```text
wss://coolify.provausio.com/app/...
```

However, the realtime request was not correctly routed to the Soketi service.

### Resolution

Added a custom location to the Nginx Proxy Manager host for `coolify.provausio.com`.

**NPM → Proxy Hosts → coolify.provausio.com → Custom Locations**

| Setting | Value |
|---|---|
| Location | `/app/` |
| Scheme | HTTP |
| Forward hostname/IP | `192.168.1.181` |
| Forward port | 6001 |
| Custom Nginx configuration | Empty |

WebSocket support remains enabled on the main proxy host.

### Critical troubleshooting discovery

The custom location was initially configured with the incorrect IP:

```text
192.168.181.1
```

The correct IP is:

```text
192.168.1.181
```

This typo caused WebSocket requests through NPM to time out.

After correcting the address, the realtime error stopped appearing.

### Direct WebSocket test

```bash
curl -i --max-time 5 \
  -H "Connection: Upgrade" \
  -H "Upgrade: websocket" \
  -H "Sec-WebSocket-Version: 13" \
  -H "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==" \
  http://127.0.0.1:6001/app/test
```

Expected initial response:

```text
HTTP/1.1 101 Switching Protocols
```

The subsequent response:

```json
{
  "event": "pusher:error",
  "data": {
    "code": 4001,
    "message": "App key test does not exist."
  }
}
```

is expected when testing with the intentionally invalid app key `test`.

### NPM verification

```bash
docker exec nginx-proxy-manager-nginx-1 nginx -t
```

The final configuration passed syntax validation.

HTTP/2 deprecation warnings were present but did not prevent Nginx from loading.

## 8. GitHub Integration

Coolify uses a registered GitHub App to access repositories and receive deployment webhooks.

### GitHub App registration

The GitHub App was registered using a temporary public Coolify endpoint through Cloudflare Tunnel.

During registration, Nginx Proxy Manager's webhook-only restrictions were temporarily relaxed so the authentication and callback flow could complete.

After registration, the public route was restricted again.

### GitHub webhook endpoint

```text
https://homelab-deploy.infinity-draw.com/webhooks/source/github/events
```

The endpoint is handled by Coolify.

### Public routing

```text
GitHub
  │
  ▼
Cloudflare
  │
  ▼
Cloudflare Tunnel
  │
  ▼
Nginx Proxy Manager :80
  │
  ▼
Coolify :30275
```

### Cloudflare Tunnel

**Public hostname:**

```text
homelab-deploy.infinity-draw.com
```

**Tunnel origin:**

```text
http://192.168.1.181:80
```

The Cloudflare Tunnel connector runs as a TrueNAS Custom App using:

```text
cloudflare/cloudflared:latest
```

Command:

```text
tunnel --no-autoupdate run
```

The tunnel token is supplied through the `TUNNEL_TOKEN` environment variable.

No UniFi router port forwarding is required.

### Webhook-only NPM configuration

The public webhook hostname forwards to:

```text
http://192.168.1.181:30275
```

With these restrictions in the NPM Advanced configuration:

```nginx
location = /webhooks/source/github/events {
    if ($request_method != POST) {
        return 404;
    }

    proxy_pass http://192.168.1.181:30275;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto https;
}

location / {
    return 404;
}
```

This exposes the webhook endpoint without exposing the Coolify administration interface through the public Cloudflare hostname.

Coolify handles GitHub webhook signature validation using its registered GitHub App secret.

### Verification

Public administration route:

```bash
curl -I https://homelab-deploy.infinity-draw.com/
```

Expected:

```text
404 Not Found
```

The GitHub webhook route should remain accessible for valid authenticated POST requests.

## 9. Application Deployment

Applications are managed through Coolify projects and environments.

For GitHub-connected applications, use:

```text
New Resource → Git Repository (with GitHub App)
```

Coolify supports Dockerfile and Docker Compose builds.

### Docker Compose deployments

Applications using a Compose build strategy configure networking through their Compose YAML.

Example:

```yaml
services:
  homelab:
    build:
      context: .
      dockerfile: src/Api/Homelab.Api/Dockerfile

    expose:
      - "8080"

    ports:
      - "18080:8080"
```

This publishes the application's internal port 8080 on TrueNAS host port 18080.

Nginx Proxy Manager can then forward to:

```text
http://192.168.1.181:18080
```

The host port mapping must be included in the Compose configuration used by Coolify.

For Git-managed Compose applications, verify the effective configuration using **Show Deployable Compose**.

### Deployment tradeoff

Publishing fixed host ports can interfere with zero-downtime deployments because multiple container instances cannot simultaneously bind the same host port.

For future improvement, NPM could connect directly to the appropriate application Docker network, avoiding host port publication.

## 10. Known Issues and Maintenance

### Reverb port conflict

The main Coolify container repeatedly logged:

```text
Failed to listen on "tcp://0.0.0.0:6002":
Address in use
```

Investigation confirmed that its terminal server was already listening on port 6002.

This prevented Laravel Reverb from binding to the same port.

The issue was not directly resolved.

The realtime UI is currently functioning through the separate Soketi service on port 6001.

### TrueNAS Community App limitations

The installation required manual corrections:

1. Creating a missing SSH private key association.
2. Creating the missing `coolify` Docker network.
3. Adding a terminal WebSocket proxy.
4. Adjusting the TrueNAS realtime port mapping.
5. Configuring NPM realtime WebSocket routing.

These changes may need to be revisited after application upgrades.

### Upgrade checklist

Before upgrading Coolify or TrueNAS:

- Back up Coolify PostgreSQL and persistent application data.
- Record the current container image versions.
- Verify the GitHub App credentials and SSH private keys are preserved.
- Check whether the TrueNAS app now provisions the `coolify` Docker network.
- Confirm whether the terminal proxy workaround is still necessary.
- Verify port mappings for 6001, 6002, and 16003.
- Verify NPM's `/app/` routing still works.
- Test GitHub webhook delivery and a deployment.
- Confirm existing Dockerge-managed applications are unaffected.

## 11. Useful Diagnostic Commands

### List Coolify containers

```bash
docker ps --format \
  'table {{.Names}}\t{{.Status}}\t{{.Ports}}' \
  | grep coolify
```

### Inspect networks

```bash
docker network ls
docker network inspect coolify
```

### Check NPM configuration

```bash
docker exec nginx-proxy-manager-nginx-1 nginx -t
```

### Check NPM generated proxy configuration

```bash
docker exec nginx-proxy-manager-nginx-1 \
  cat /data/nginx/proxy_host/16.conf
```

Proxy host file `16.conf` was the relevant file during troubleshooting. Its number may change if NPM hosts are recreated.

### Check Coolify logs

```bash
docker logs --tail 100 ix-coolify-coolify-1
```

### Check Laravel logs

```bash
docker exec ix-coolify-coolify-1 \
  tail -n 100 /var/www/html/storage/logs/laravel.log
```

### Check realtime logs

```bash
docker logs --tail 100 ix-coolify-realtime-1
```

### Test Coolify's HTTP endpoint

```bash
curl -I http://127.0.0.1:30275
```

### Test HTTPS through NPM

```bash
curl -I https://coolify.provausio.com
```

### Inspect served TLS certificate

```bash
echo | openssl s_client \
  -connect coolify.provausio.com:443 \
  -servername coolify.provausio.com 2>/dev/null \
  | openssl x509 -noout -subject -issuer -ext subjectAltName
```

## 12. Final Operating Model

**Dockerge**

Manages existing homelab infrastructure and services.

**Coolify**

Manages custom-developed applications, Git integration, Docker builds, and automated deployments.

**Nginx Proxy Manager**

Handles application routing, internal domain names, and HTTPS certificates.

**Cloudflare Tunnel**

Provides externally accessible GitHub webhook delivery without inbound router port forwarding.

### Target developer workflow

```text
Developer commits code
        │
        ▼
GitHub repository
        │
        ▼
GitHub webhook
        │
        ▼
Cloudflare Tunnel
        │
        ▼
Nginx Proxy Manager
        │
        ▼
Coolify
        │
        ├── Pull latest source
        ├── Build Docker image
        ├── Start updated container
        └── Manage deployment
        │
        ▼
Application running on TrueNAS
```

The core goal is to eliminate manual Docker image publishing and manual Dockerge updates for custom-developed applications.