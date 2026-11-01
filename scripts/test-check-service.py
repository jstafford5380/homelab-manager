"""Local synthetic HTTPS contract tests; never contacts a NAS or notification service."""
import http.server
import importlib.util
import json
from pathlib import Path
import ssl
import subprocess
import sys
import tempfile
import threading
import time
import unittest

spec = importlib.util.spec_from_file_location("checker", Path(__file__).with_name("check-service.py"))
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)


class Handler(http.server.BaseHTTPRequestHandler):
    mode = "ready"
    requests = []

    def log_message(self, *args):
        pass

    def do_GET(self):
        type(self).requests.append(self.path)
        mode = type(self).mode
        if mode == "slow":
            time.sleep(2)
        body = b'{"status":"ready"}'
        status = 200
        if mode == "redirect":
            status = 302
        if mode == "unavailable":
            status = 503
        if mode == "wrong-body":
            body = b'{"status":"alive"}'
        if mode == "oversized":
            body = b" " * 4097
        if mode == "invalid-json":
            body = b"not JSON"
        try:
            self.send_response(status)
            if mode == "redirect":
                self.send_header("Location", "/redirect-must-not-be-followed")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except OSError:
            pass


class CheckerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.folder = tempfile.TemporaryDirectory(prefix="homelab-probe-test-")
        cls.cert = str(Path(cls.folder.name) / "tls.crt")
        key = str(Path(cls.folder.name) / "tls.key")
        subprocess.run(["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-keyout", key,
            "-out", cls.cert, "-days", "1", "-subj", "/CN=localhost", "-addext", "subjectAltName=DNS:localhost,IP:127.0.0.1"],
            check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        cls.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(cls.cert, key)
        cls.server.socket = context.wrap_socket(cls.server.socket, server_side=True)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.url = f"https://127.0.0.1:{cls.server.server_port}/health/ready"

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.thread.join()
        cls.folder.cleanup()

    def setUp(self):
        Handler.mode = "ready"
        Handler.requests = []

    def test_ready_and_untrusted_tls(self):
        result = checker.check(self.url, self.cert, 5)
        self.assertEqual("ready", result["status"])
        self.assertEqual(["/health/ready"], Handler.requests)
        self.assertNotIn("127.0.0.1", json.dumps(result))
        self.assertEqual("tls_validation_failed", checker.check(self.url, timeout=5)["reason"])

    def test_unavailable_redirect_and_malformed_responses(self):
        for mode, reason in [("unavailable", "unexpected_http_status"), ("redirect", "unexpected_http_status"),
                ("wrong-body", "unexpected_readiness_body"), ("oversized", "response_too_large"), ("invalid-json", "invalid_or_unavailable_response")]:
            with self.subTest(mode=mode):
                Handler.mode = mode
                result = checker.check(self.url, self.cert, 5)
                self.assertEqual("not_ready", result["status"])
                self.assertEqual(reason, result["reason"])
        self.assertNotIn("/redirect-must-not-be-followed", Handler.requests)

    def test_total_deadline_kills_slow_probe(self):
        Handler.mode = "slow"
        started = time.monotonic()
        result = checker.check(self.url, self.cert, 0.4)
        self.assertEqual("deadline_exceeded", result["reason"])
        self.assertLess(time.monotonic() - started, 1.5)

    def test_cli_configuration_error_is_nonzero_and_does_not_echo_input(self):
        result = subprocess.run([sys.executable, str(Path(__file__).with_name("check-service.py")),
            "https://owner:PRIVATE-FIXTURE@localhost/health/ready"], capture_output=True, text=True, timeout=5)
        self.assertEqual(2, result.returncode)
        self.assertEqual("configuration_error", json.loads(result.stdout)["status"])
        self.assertNotIn("PRIVATE-FIXTURE", result.stdout + result.stderr)
        self.assertEqual([], Handler.requests)

    def test_invalid_configuration_never_probes(self):
        for url in ["http://localhost/health/ready", "https://name:secret@localhost/health/ready", "https://localhost/health/ready?secret=x",
                    "https://localhost/health/live", "https://localhost/health/ready#fragment", "https://localhost:0/health/ready"]:
            with self.subTest(url=url), self.assertRaises(ValueError):
                checker.check(url, timeout=1)
        for timeout in [0, 61, float("nan"), float("inf")]:
            with self.assertRaises(ValueError):
                checker.check(self.url, timeout=timeout)
        self.assertEqual([], Handler.requests)


if __name__ == "__main__":
    unittest.main()
