"""Start a disposable local application instance for browser tests."""
import os
import pathlib
import signal
import subprocess
import tempfile
root = pathlib.Path(__file__).resolve().parents[1]
with tempfile.TemporaryDirectory(prefix="homelab-e2e-") as data:
    env = os.environ.copy()
    env.update(ASPNETCORE_ENVIRONMENT="Development", Homelab__DataPath=data, Homelab__SetupToken="browser-test-setup-only")
    process = subprocess.Popen(["dotnet", "run", "--project", str(root / "src/Homelab.Api"), "--no-build", "--no-launch-profile", "--urls", "http://127.0.0.1:5099"], env=env)
    def stop(*_):
        process.terminate()
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    try:
        process.wait()
    finally:
        if process.poll() is None:
            process.terminate()
            process.wait(timeout=15)
