"""Prepare private local Docker deployment files without overwriting existing secrets."""
import os
import pathlib
import secrets
import subprocess
root = pathlib.Path(__file__).resolve().parents[1]
folder = root / "secrets"
if folder.exists() or (root / ".env").exists():
    raise SystemExit("Existing secrets or .env found; refusing to overwrite. Follow rotation instructions instead.")
folder.mkdir(mode=0o700)
try:
    for name, days in [("key-encryption", "3650"), ("tls", "365")]:
        password = folder / (name + ".password")
        password.write_text(secrets.token_urlsafe(48)); password.chmod(0o600)
        private = folder / (name + ".key")
        certificate = folder / (name + ".crt")
        subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:3072", "-nodes", "-keyout", str(private), "-out", str(certificate), "-days", days, "-subj", "/CN=localhost", "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1"], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        private.chmod(0o600)
        output = folder / (name + ".pfx")
        subprocess.run(["openssl", "pkcs12", "-export", "-out", str(output), "-inkey", str(private), "-in", str(certificate), "-passout", "file:" + str(password)], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        output.chmod(0o600); private.unlink()
    token = folder / "setup-token"; token.write_text(secrets.token_urlsafe(32)); token.chmod(0o600)
    for name in ["data", "backups"]:
        (root / name).mkdir(mode=0o700, exist_ok=True)
    env = root / ".env"; env.write_text(f"HOMELAB_UID={os.getuid()}\nHOMELAB_GID={os.getgid()}\n"); env.chmod(0o600)
    print("Prepared local deployment. Setup token is in secrets/setup-token; it was not printed. Open https://localhost:8443 after starting Compose. Trust secrets/tls.crt only after checking its fingerprint. Backups must be moved to independent storage before relying on them.")
except BaseException:
    print("Preparation failed. Inspect the private secrets directory before retrying; nothing will be overwritten automatically.")
    raise
