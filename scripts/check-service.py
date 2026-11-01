"""Independent, one-shot HTTPS readiness probe. No NAS access or notification delivery."""
import argparse
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def validate(url, timeout):
    try:
        parsed = urllib.parse.urlsplit(url)
        port = parsed.port
        valid = (parsed.scheme == "https" and parsed.hostname and parsed.username is None
                 and parsed.password is None and parsed.path == "/health/ready"
                 and not parsed.query and not parsed.fragment and port != 0
                 and not any(ord(char) < 33 or ord(char) == 127 for char in url)
                 and len(url) <= 2048 and math.isfinite(timeout) and 0.1 <= timeout <= 60)
    except (ValueError, TypeError):
        valid = False
    if not valid:
        raise ValueError("Use an HTTPS /health/ready URL without credentials, query or fragment, and a timeout from 0.1 to 60 seconds.")


def probe(url, ca_file, timeout):
    try:
        context = ssl.create_default_context(cafile=ca_file)
    except (OSError, ssl.SSLError):
        return {"status": "not_ready", "reason": "trust_configuration_error"}
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(), urllib.request.HTTPSHandler(context=context))
    try:
        request = urllib.request.Request(url, headers={"Accept": "application/json", "Accept-Encoding": "identity", "User-Agent": "Homelab-Control-Readiness/1"})
        with opener.open(request, timeout=timeout) as response:
            if response.status != 200:
                return {"status": "not_ready", "reason": "unexpected_http_status", "httpStatus": response.status}
            body = response.read(4097)
            if len(body) > 4096:
                return {"status": "not_ready", "reason": "response_too_large"}
            payload = json.loads(body)
            if not isinstance(payload, dict) or payload.get("status") != "ready":
                return {"status": "not_ready", "reason": "unexpected_readiness_body"}
            return {"status": "ready", "reason": "readiness_confirmed"}
    except urllib.error.HTTPError as error:
        return {"status": "not_ready", "reason": "unexpected_http_status", "httpStatus": error.code}
    except urllib.error.URLError as error:
        reason = "tls_validation_failed" if isinstance(error.reason, ssl.SSLCertVerificationError) else "connection_failed"
        return {"status": "not_ready", "reason": reason}
    except (TimeoutError, OSError, ValueError):
        return {"status": "not_ready", "reason": "invalid_or_unavailable_response"}


def check(url, ca_file=None, timeout=10):
    validate(url, timeout)
    started = time.monotonic()
    try:
        # The outer process bounds DNS, TLS, headers and body together, including slow streams.
        result = subprocess.run([sys.executable, str(Path(__file__).resolve()), "--worker"],
            input=json.dumps({"url": url, "ca_file": ca_file, "timeout": timeout}), text=True,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout, check=False)
        report = json.loads(result.stdout) if result.returncode == 0 else {"status": "not_ready", "reason": "probe_failed"}
    except subprocess.TimeoutExpired:
        report = {"status": "not_ready", "reason": "deadline_exceeded"}
    except (OSError, ValueError):
        report = {"status": "not_ready", "reason": "probe_failed"}
    report.update(checkedUtc=datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"), elapsedSeconds=round(time.monotonic()-started, 3))
    return report


def main():
    if sys.argv[1:] == ["--worker"]:
        config = json.loads(sys.stdin.read(8192))
        validate(config["url"], config["timeout"])
        print(json.dumps(probe(**config)))
        return 0
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("url", help="Explicit HTTPS readiness URL on the control-service host")
    parser.add_argument("--ca-file", help="Trusted CA PEM file; defaults to system trust")
    parser.add_argument("--timeout", type=float, default=10, help="Total probe deadline, seconds (default 10, maximum 60)")
    args = parser.parse_args()
    try:
        report = check(args.url, args.ca_file, args.timeout)
    except ValueError as error:
        print(json.dumps({"status": "configuration_error", "reason": str(error)}))
        return 2
    print(json.dumps(report))
    return 0 if report["status"] == "ready" else 1


if __name__ == "__main__":
    sys.exit(main())
