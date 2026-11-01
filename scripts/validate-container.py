"""Exercise production Compose, restart and isolated restore using disposable local data.
Build homelab-control:mvp-validation first. Only a disabled synthetic NAS profile is
seeded in disposable local data; no NAS is contacted.
This is a same-host container exercise, not the independent-host restore release gate.
"""
import argparse
from datetime import datetime, timedelta, timezone
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import re
import sqlite3
import zipfile
import shutil
import ssl
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from urllib.parse import urlencode
import uuid

ROOT = Path(__file__).resolve().parents[1]

def command(args):
    return subprocess.run(args, check=True, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout.strip()

class Deployment:
    def __init__(self, folder, image):
        self.folder = folder
        (folder / "scripts").mkdir(parents=True)
        shutil.copy2(ROOT / "scripts/prepare-local-deployment.py", folder / "scripts/prepare-local-deployment.py")
        command([sys.executable, str(folder / "scripts/prepare-local-deployment.py")])
        with (folder / ".env").open("a") as env:
            env.write("HOMELAB_HTTPS_PORT=0\n")
        override = folder / "override.json"
        override.write_text(json.dumps({"services":{"homelab":{"image":image}}}))
        self.compose = ["docker", "compose", "--project-directory", str(folder), "--env-file", str(folder / ".env"), "-p", "homelab-check-" + secrets.token_hex(4), "-f", str(ROOT / "compose.yaml"), "-f", str(override)]
        self.cookies = http.cookiejar.CookieJar()
        context = ssl.create_default_context(cafile=str(folder / "secrets/tls.crt"))
        self.client = urllib.request.build_opener(urllib.request.ProxyHandler({}), urllib.request.HTTPSHandler(context=context), urllib.request.HTTPCookieProcessor(self.cookies))
        self.base = ""
    def start(self):
        command(self.compose + ["up", "-d", "--no-build"])
        address = command(self.compose + ["port", "homelab", "8443"])
        assert address.startswith("127.0.0.1:"), "Deployment must bind to loopback"
        self.base = "https://" + address
        self.ready()
    def ready(self):
        # Docker may assign a new host port when restarting a port-zero binding.
        address = command(self.compose + ["port", "homelab", "8443"])
        assert address.startswith("127.0.0.1:")
        self.base = "https://" + address
        deadline = time.monotonic() + 40
        while time.monotonic() < deadline:
            try:
                if self.request("/health/ready")[0] == 200: return
            except (OSError, urllib.error.URLError): pass
            time.sleep(.2)
        print(command(self.compose + ["logs", "--tail", "40", "homelab"]), file=sys.stderr)
        raise RuntimeError("Container did not become ready within 40 seconds")
    def request(self, path, data=None, method=None):
        headers = {"X-Homelab-Request":"1"}
        body = None
        if data is not None:
            headers["Content-Type"] = "application/json"
            body = json.dumps(data).encode()
        request = urllib.request.Request(self.base + path, data=body, headers=headers, method=method)
        try:
            with self.client.open(request, timeout=3) as response:
                content = response.read()
                result = json.loads(content) if content and "json" in response.headers.get("Content-Type", "") else content
                return response.status, result
        except urllib.error.HTTPError as error:
            return error.code, None
    def verify_isolation(self):
        container = command(self.compose + ["ps", "-q", "homelab"])
        config = json.loads(command(["docker", "inspect", container]))[0]
        assert not any(v in config["Config"]["Env"] for v in ["ASPNETCORE_ENVIRONMENT=Development", "DOTNET_ENVIRONMENT=Development"])
        assert config["Config"]["User"] == f"{os.getuid()}:{os.getgid()}"
        assert os.getuid() != 0, "Run validation as a non-root user"
        assert "ALL" in config["HostConfig"]["CapDrop"]
        assert any(x.startswith("no-new-privileges") for x in config["HostConfig"]["SecurityOpt"])
        assert not any("docker.sock" in m["Source"] for m in config["Mounts"])
        for mount in config["Mounts"]:
            if mount["Destination"].startswith("/run/secrets/"): assert not mount["RW"]
        return container
    def stop(self):
        subprocess.run(self.compose + ["down", "--remove-orphans"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

def seed_monitoring_evidence(deployment):
    """Offline fixture only: never expose a seeding endpoint or enroll a live credential."""
    command(deployment.compose + ["stop", "homelab"])
    nas_id = str(uuid.uuid4()).upper()
    disk_id = str(uuid.uuid4()).upper()
    other_disk_id = str(uuid.uuid4()).upper()
    nas_revision = str(uuid.uuid4()).upper()
    now = datetime.now(timezone.utc)
    stored_time = lambda value: value.strftime("%Y-%m-%d %H:%M:%S.%f")
    fetched = now.isoformat().replace("+00:00", "Z")
    snapshot = {"FetchedUtc": fetched, "Items": [{"Name":"fixture/photos", "Pool":"fixture", "Type":"FILESYSTEM",
                "UsedBytes":1073741824, "AvailableBytes":None, "SnapshotUsedBytes":0, "QuotaBytes":0, "RefQuotaBytes":None}]}
    with sqlite3.connect(deployment.folder / "data/homelab.db") as database:
        database.execute("PRAGMA foreign_keys=ON")
        database.execute("""INSERT INTO Connections
            (Id, Name, ApiUri, ProtectedApiKey, Status, PollSeconds, Enabled, ConsecutiveFailures,
             SystemIdentityState, ConfigurationRevision, DatasetsJson)
            VALUES (?, ?, ?, ?, ?, 60, 0, 0, 'unavailable', ?, ?)""",
            (nas_id, "Disabled monitoring fixture", "wss://nas-fixture.invalid/websocket", "synthetic-unusable-credential",
             "Synthetic history only — collection disabled", nas_revision, json.dumps(snapshot)))
        database.execute("""INSERT INTO DatasetSamples (ConnectionId, ConfigurationRevision, HourUtc, FetchedUtc, SystemSerial, Version, SnapshotJson)
            VALUES (?, ?, ?, ?, 'SYNTHETIC-NAS', '24.10.2', ?)""", (nas_id,nas_revision,stored_time(now.replace(minute=0,second=0,microsecond=0)),stored_time(now),json.dumps(snapshot)))
        old_snapshot=json.loads(json.dumps(snapshot));old_snapshot["FetchedUtc"]=(now-timedelta(hours=1)).isoformat().replace("+00:00","Z");old_snapshot["Items"][0]["Name"]="fixture/old"
        database.execute("""INSERT INTO DatasetSamples (ConnectionId, ConfigurationRevision, HourUtc, FetchedUtc, SystemSerial, Version, SnapshotJson)
            VALUES (?, ?, ?, ?, 'SYNTHETIC-NAS', '24.10.2', ?)""",(nas_id,nas_revision,stored_time(now.replace(minute=0,second=0,microsecond=0)-timedelta(hours=1)),stored_time(now-timedelta(hours=1)),json.dumps(old_snapshot)))
        database.execute("""INSERT INTO Disks
            (Id, ConnectionId, IdentityKey, Serial, Model, Alias, MappingConfidence,
             BayVerified, Present, FirstSeenUtc, LastSeenUtc, Notes, Lifecycle, PurchaseDateConfidence)
            VALUES (?, ?, 'container-drive', 'CONTAINER-DRIVE', 'Synthetic drive', '', 'inferred',
                    0, 1, ?, ?, '', 'installed', 'unknown')""",
            (disk_id, nas_id, stored_time(now), stored_time(now)))
        database.execute("""INSERT INTO Disks (Id, ConnectionId, IdentityKey, Serial, Model, Alias, MappingConfidence, BayVerified, Present, FirstSeenUtc, LastSeenUtc, Notes, Lifecycle, PurchaseDateConfidence)
            VALUES (?, ?, 'container-other', 'CONTAINER-OTHER', 'Synthetic other record', '', 'inferred', 0, 1, ?, ?, '', 'installed', 'unknown')""", (other_disk_id,nas_id,stored_time(now),stored_time(now)))
        database.execute("UPDATE Disks SET Wwn=? WHERE Id=?", ("5000c50012345678",disk_id))
        identity_snapshot={"revision":nas_revision,"fetchedUtc":fetched,"rows":[{"serial":"CONTAINER-DRIVE","model":"Synthetic drive","wwn":"5000c50012345678","alias":"sda"}]}
        database.execute("UPDATE Connections SET DiscoveryIdentityJson=? WHERE Id=?",(json.dumps(identity_snapshot),nas_id))
        database.execute("""INSERT INTO DiscoveryAssignments (Id, ConnectionId, DiskId, ConfigurationRevision, RequestId, SubmissionHash, Wwn, Serial, Model, Evidence, Author, RecordedUtc, ValidUntilUtc, RevokedUtc, RevocationEvidence, RevokedBy)
            VALUES (?, ?, ?, ?, ?, 'offline-synthetic-hash', '5000c50012345678', 'CONTAINER-DRIVE', 'Synthetic drive', 'Synthetic identity label review', 'fixture-owner', ?, ?, ?, 'Synthetic review revoked before backup', 'fixture-owner')""", (str(uuid.uuid4()).upper(),nas_id,disk_id,nas_revision,str(uuid.uuid4()).upper(),stored_time(now-timedelta(minutes=2)),stored_time(now+timedelta(days=30)),stored_time(now-timedelta(minutes=1))))
        database.execute("""INSERT INTO PoolScans
            (ConnectionId, PoolGuid, ScanFunction, ScanStartedUtc, ScanState, ScanEndedUtc,
             ScanPercentage, ScanErrors, ScanErrorsPeak, FirstFetchedUtc, LastFetchedUtc, SourceSchema)
            VALUES (?, 'fixture-pool', 'SCRUB', ?, 'FINISHED', ?, 100, 0, 7, ?, ?, 'synthetic-container-fixture')""",
            (nas_id, stored_time(now - timedelta(hours=3)), stored_time(now - timedelta(hours=1)),
             stored_time(now - timedelta(hours=2)), stored_time(now)))
        member_revision = str(uuid.uuid4())
        for index, errors in enumerate((2, 7)):
            at = now - timedelta(seconds=62-index*60)
            iso = lambda value: value.isoformat().replace("+00:00", "Z")
            node = {"guid":"18446744073709551615", "parentGuid":None, "role":"data", "type":"DISK", "state":"ONLINE", "path":None, "device":None, "diskAlias":None, "readErrors":errors, "writeErrors":None, "checksumErrors":0}
            node["driveMapping"]={"status":"inferred","diskId":disk_id,"serial":"CONTAINER-DRIVE","wwn":"5000c50012345678","model":"Synthetic drive","explanation":"Synthetic historical mapping only"}
            member = {"schema":"truenas-24.10-vdev-evidence-v1", "status":"reported", "explanation":"Synthetic member history", "nodes":[node]}
            context = {"schema":"truenas-24.10-system-context-v1", "serial":"SYNTHETIC-NAS", "version":"24.10.2", "configurationRevision":member_revision, "readStartedUtc":iso(at), "readFinishedUtc":iso(at+timedelta(seconds=1)), "uptimeSeconds":10000+index*60}
            database.execute("""INSERT INTO PoolSnapshots (ConnectionId, PoolGuid, Name, Status, FetchedUtc, SourceSchema, VdevEvidenceJson, SourceContextJson)
                VALUES (?, 'fixture-pool', 'fixture', 'ONLINE', ?, 'truenas-24.10-pool-v4', ?, ?)""", (nas_id,stored_time(at+timedelta(seconds=2)),json.dumps(member),json.dumps(context)))
        assert database.execute("SELECT COUNT(*) FROM CollectionRuns").fetchone()[0] == 0
    deployment.start()
    return nas_id, disk_id, other_disk_id

def verify_monitoring_evidence(deployment, nas_id):
    status, profiles = deployment.request("/api/v1/connections")
    assert status == 200 and len(profiles) == 1
    assert profiles[0]["id"].upper() == nas_id and profiles[0]["enabled"] is False
    assert profiles[0]["lastSuccessUtc"] is None
    path = "/api/v1/connections/" + nas_id
    status, datasets = deployment.request(path + "/datasets")
    assert status == 200 and datasets["enabled"] is True and datasets["status"] == "paused"
    item = datasets["snapshot"]["items"][0]
    assert len(datasets["snapshot"]["items"]) == 1
    assert item["name"] == "fixture/photos" and item["usedBytes"] == 1073741824
    assert item["availableBytes"] is None and item["snapshotUsedBytes"] == 0
    assert item["quotaBytes"] == 0 and item["refQuotaBytes"] is None
    assert datasets["snapshot"]["fetchedUtc"].endswith("Z")
    status, scans = deployment.request(path + "/pools/fixture-pool/scans")
    assert status == 200 and len(scans["items"]) == 1 and scans["nextBefore"] is None
    scan = scans["items"][0]
    assert scan["scanState"] == "FINISHED" and scan["scanErrors"] == 0 and scan["scanErrorsPeak"] == 7
    assert scan["scanStartedUtc"].endswith("Z") and scan["scanEndedUtc"].endswith("Z")
    assert scan["sourceSchema"] == "synthetic-container-fixture"
    status, history = deployment.request(path + "/collection-history")
    assert status == 200 and history["items"] == [], "Disabled fixture must never be collected"
    settings = deployment.request("/api/v1/settings")[1]["settings"]
    assert settings["temperatureReviewCelsius"] == 45
    assert settings["temperaturePolicyReason"] == "Synthetic local policy for restore validation"
    assert settings["readDatasets"] is True
    status, members = deployment.request(path + "/pools/fixture-pool/history")
    assert status == 200 and len(members["items"]) == 2
    latest = members["items"][0]
    assert "sourceContextJson" not in latest
    assert latest["vdevComparison"]["status"] == "compared"
    change = latest["vdevComparison"]["members"][0]
    assert change["guid"] == "18446744073709551615"
    assert change["read"]["difference"] == 5 and change["write"]["difference"] is None
    assert members["items"][1]["vdevComparison"]["status"] == "baseline"
    status, sampled = deployment.request(path + "/dataset-history")
    assert status == 200 and len(sampled["items"]) == 2 and sampled["nextBefore"] is None
    assert next(x for x in sampled["items"] if x["snapshot"]["items"][0]["name"]=="fixture/photos")["snapshot"] == datasets["snapshot"]
    assert sampled["items"][0]["systemSerial"] == "SYNTHETIC-NAS" and sampled["items"][0]["version"] == "24.10.2"
    return datasets["snapshot"], scan, members, sampled["items"]

def record_dataset_rename(deployment,nas_id):
    path=f"/api/v1/connections/{nas_id}/dataset-renames"
    items=deployment.request(f"/api/v1/connections/{nas_id}/dataset-history")[1]["items"]
    old=next(x for x in items if x["snapshot"]["items"][0]["name"]=="fixture/old")
    new=next(x for x in items if x["snapshot"]["items"][0]["name"]=="fixture/photos")
    status,preview=deployment.request(path+f"/review?fromSampleId={old['id']}&toSampleId={new['id']}");assert status==200
    body={"fromSampleId":old["id"],"toSampleId":new["id"],"fromName":"fixture/old","toName":"fixture/photos","confirmedFromName":"fixture/old","confirmedToName":"fixture/photos","reviewToken":preview["reviewToken"],"evidence":"Synthetic independent rename log","confirmedSameDataset":True,"requestId":str(uuid.uuid4())}
    status,record=deployment.request(path,body);assert status==200
    assert deployment.request(path,body)[1]["id"]==record["id"]
    assert deployment.request(path+f"/{record['id']}/reverse",{"evidence":"Synthetic correction preserves original review"})[0]==200
    return deployment.request(path)[1]["items"]

def record_identity_and_paths(deployment, nas_id, disk_id, other_disk_id):
    status, component=deployment.request("/api/v1/inventory/assets",{"name":"Shared controller fixture","type":"controller"})
    assert status==200
    for target, serial in ((disk_id,"CONTAINER-DRIVE"),(other_disk_id,"CONTAINER-OTHER")):
        path=f"/api/v1/disks/{target}/hardware-paths"
        body={"requestId":str(uuid.uuid4()),"assetId":component["id"],"assetUpdatedUtc":component["updatedUtc"],"confirmedIdentity":serial,"attached":True,"evidence":"Synthetic cable-route evidence","validUntilUtc":(datetime.now(timezone.utc)+timedelta(days=30)).isoformat().replace("+00:00","Z")}
        status, saved=deployment.request(path,body);assert status==200, (status,saved)
        if target==disk_id:
            body.update({"requestId":str(uuid.uuid4()),"previousReviewId":saved["id"],"evidence":"Synthetic corrected cable-route evidence"})
            assert deployment.request(path,body)[0]==200
    path=f"/api/v1/disks/{disk_id}/identity-history"
    for cycle in range(2):
        status, review=deployment.request(path+"/review?otherId="+other_disk_id);assert status==200
        status, merged=deployment.request(path+"/merge",{"otherDiskId":other_disk_id,"requestId":str(uuid.uuid4()),"reviewToken":review["reviewToken"],"firstIdentity":"CONTAINER-DRIVE","secondIdentity":"CONTAINER-OTHER","evidence":"Synthetic reviewed identity grouping","confirmedSamePhysicalDrive":True});assert status==200
        if cycle==0:
            group=deployment.request(path)[1]
            assert deployment.request(path+"/"+merged["id"]+"/reverse",{"reviewToken":group["reviewToken"],"evidence":"Synthetic reversal retained"})[0]==200
    return verify_identity_and_paths(deployment,nas_id,disk_id)

def verify_identity_and_paths(deployment,nas_id,disk_id):
    status, paths=deployment.request(f"/api/v1/disks/{disk_id}/hardware-paths");assert status==200
    paths.pop("generatedUtc")
    assert len(paths["items"])==2 and paths["items"][1]["status"]=="superseded"
    assert len(paths["peers"])==1
    status, identities=deployment.request(f"/api/v1/disks/{disk_id}/identity-history");assert status==200
    assert len(identities["members"])==2 and len(identities["links"])==2
    assert sum(link["reversedUtc"] is not None for link in identities["links"])==1
    assert all(row["diskId"].upper()==disk_id for row in identities["observations"])
    status, discovery=deployment.request(f"/api/v1/connections/{nas_id}/identity-review");assert status==200
    assert discovery["usable"] is False and len(discovery["assignments"])==1
    assert discovery["assignments"][0]["revocationEvidence"]=="Synthetic review revoked before backup"
    return paths, identities, discovery

def record_local_drive_evidence(deployment, disk_id, asset):
    """Use production authenticated APIs for records; only physical discovery is seeded offline."""
    path = "/api/v1/disks/" + disk_id
    now = datetime.now(timezone.utc)
    timestamp = lambda value: value.isoformat().replace("+00:00", "Z")
    maintenance = {"requestId":str(uuid.uuid4()), "occurredUtc":timestamp(now-timedelta(hours=1)),
                   "dateConfidence":"estimated", "kind":"inspection", "outcome":"unknown",
                   "summary":"Synthetic inspection", "evidence":"PRIVATE-MAINTENANCE-FIXTURE",
                   "confirmedIdentity":"CONTAINER-DRIVE"}
    status, saved = deployment.request(path + "/maintenance", maintenance)
    assert status == 200
    assert deployment.request(path + "/maintenance", maintenance)[1]["id"] == saved["id"]
    assert deployment.request(path + "/maintenance/" + str(saved["id"]) + "/void",
                              {"reason":"Synthetic correction retained through restore"})[0] == 200
    review = {"requestId":str(uuid.uuid4()), "assetId":asset["id"], "assetUpdatedUtc":asset["updatedUtc"],
              "confirmedIdentity":"CONTAINER-DRIVE", "verdict":"compatible",
              "evidence":"PRIVATE-SPARE-FIXTURE: synthetic capacity/interface/sector/fit review",
              "validUntilUtc":timestamp(now+timedelta(days=30))}
    assert deployment.request(path + "/spare-reviews",review)[0] == 200
    # A correction must supersede the first review without deleting its evidence.
    review.update({"requestId":str(uuid.uuid4()), "verdict":"unknown", "evidence":"PRIVATE-SPARE-CORRECTION: requirements need investigation"})
    status, saved = deployment.request(path + "/spare-reviews",review)
    assert status == 200
    assert deployment.request(path + "/spare-reviews",review)[1]["id"] == saved["id"]
    assert deployment.request(path + "/spare-reviews",dict(review, verdict="compatible"))[0] == 409
    return verify_local_drive_evidence(deployment, disk_id, asset)

def verify_local_drive_evidence(deployment, disk_id, asset):
    path = "/api/v1/disks/" + disk_id
    status, maintenance = deployment.request(path + "/maintenance")
    assert status == 200 and maintenance["nextBefore"] is None and len(maintenance["items"]) == 1
    item = maintenance["items"][0]
    assert item["evidence"] == "PRIVATE-MAINTENANCE-FIXTURE" and item["dateConfidence"] == "estimated"
    assert item["voidedUtc"] and item["voidReason"] == "Synthetic correction retained through restore"
    assert item["author"] == "container-validation" and item["voidedBy"] == "container-validation"
    assert item["sourceSchema"] == "manual-maintenance-v1" and item["recordedUtc"].endswith("Z")
    status, reviews = deployment.request(path + "/spare-reviews")
    assert status == 200 and reviews["nextBefore"] is None and len(reviews["items"]) == 2
    assert [row["status"] for row in reviews["items"]] == ["unknown", "superseded"]
    assert [row["review"]["verdict"] for row in reviews["items"]] == ["unknown", "compatible"]
    for row in reviews["items"]:
        assert row["review"]["assetId"] == asset["id"]
        assert row["review"]["assetUpdatedUtc"] == asset["updatedUtc"]
        assert row["review"]["author"] == "container-validation"
    assert deployment.request("/api/v1/inventory")[1]["assets"][0]["quantity"] == 2
    for redacted in (False, True):
        status, export = deployment.request("/api/v1/inventory/export?redacted=" + str(redacted).lower())
        assert status == 200
        text = json.dumps(export) if not isinstance(export, bytes) else export.decode()
        for secret in ("PRIVATE-MAINTENANCE-FIXTURE", "PRIVATE-SPARE-FIXTURE", "PRIVATE-SPARE-CORRECTION"):
            assert (secret in text) is not redacted
    return maintenance, reviews["items"]

def import_smart_fixture(deployment, disk_id, report):
    path = "/api/v1/disks/" + disk_id + "/smart-reports"
    body = {"json":json.dumps(report), "confirmedSerial":"CONTAINER-DRIVE"}
    status, preview = deployment.request(path + "/preview", body)
    assert status == 200
    body["expectedHash"] = preview["reportHash"]
    assert deployment.request(path, body)[0] == 200
    return path

def record_smart_evidence(deployment, disk_id):
    report = {"json_format_version":[1,0], "smartctl":{"version":[7,4],"exit_status":0},
              "device":{"protocol":"ATA"},"serial_number":"CONTAINER-DRIVE","model_name":"Synthetic drive",
              "local_time":{"time_t":int(time.time())-300},"firmware_version":"FIXTURE-FW01","sata_version":{"string":"SATA 3.3"},
              "ata_smart_self_test_log":{"standard":{"revision":1,"table":[{"type":{"value":2,"string":"Extended offline"},
                  "status":{"value":112,"string":"Completed: read failure","passed":False},"lifetime_hours":12000,"lba":1234}]}}}
    path = import_smart_fixture(deployment, disk_id, report)
    history = deployment.request(path)[1]
    assert len(history) == 1
    curated = history[0]["report"]
    assert curated["deviceInfo"]["firmware"] == "FIXTURE-FW01"
    assert curated["deviceInfo"]["interface"] == "SATA 3.3"
    entry = curated["ataSelfTests"][0]["entries"][0]
    assert entry["result"] == "failed" and entry["firstErrorLba"] == 1234 and entry["lifetimeHours"] == 12000
    findings = [row for row in deployment.request("/api/v1/overview")[1]["findings"] if row["rule"] == "ata-self-test-history-v1"]
    assert len(findings) == 1 and findings[0]["state"] == "open"
    finding_path = "/api/v1/findings/" + findings[0]["id"]
    detail = deployment.request(finding_path)[1]
    assert deployment.request(finding_path + "/resolve", {"resolutionToken":detail["resolutionToken"],
        "evidence":"Synthetic investigation and recovery review completed", "reviewedCurrentEvidence":True})[0] == 200
    detail = deployment.request(finding_path)[1]
    assert detail["state"] == "resolved"
    return report, history, detail

def verify_restored_smart_evidence(deployment, disk_id, original):
    report, history, detail = original
    path = "/api/v1/disks/" + disk_id + "/smart-reports"
    assert deployment.request(path)[1] == history
    assert deployment.request("/api/v1/findings/" + detail["id"])[1] == detail
    report["local_time"]["time_t"] += 1  # Later report, exactly the same historical failed-test details.
    import_smart_fixture(deployment, disk_id, report)
    findings = [row for row in deployment.request("/api/v1/overview")[1]["findings"] if row["rule"] == "ata-self-test-history-v1"]
    assert len(findings) == 1 and findings[0]["state"] == "resolved"
    with sqlite3.connect(f"file:{deployment.folder / 'data/homelab.db'}?mode=ro",uri=True) as db:
        assert db.execute("SELECT COUNT(*) FROM SelfTestEvidence").fetchone()[0] == 1
        assert db.execute("PRAGMA foreign_key_check").fetchall() == []

def retained_activity(deployment, snapshot_id=None):
    query = {} if snapshot_id is None else {"snapshotId":snapshot_id}
    items = []
    for _ in range(100):
        status, page = deployment.request("/api/v1/activity/history?" + urlencode(query))
        assert status == 200 and len(page["items"]) <= 50
        if snapshot_id is None: snapshot_id = page["snapshotId"]
        assert page["snapshotId"] == snapshot_id
        items.extend(page["items"])
        if page["nextBefore"] is None:
            ids = [row["id"] for row in items]
            assert ids == sorted(set(ids), reverse=True)
            return snapshot_id, items
        query = {"snapshotId":snapshot_id, "before":page["nextBefore"]}
    raise AssertionError("Activity pagination failed to terminate")

def retained_recovery(deployment, policy_id, kind):
    status, page = deployment.request(f"/api/v1/recovery/{policy_id}/history?kind={kind}")
    assert status == 200 and len(page["items"]) == 1 and page["next"] is None
    return page["items"]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", default="homelab-control:mvp-validation")
    args = parser.parse_args()
    if os.getuid() == 0:
        raise SystemExit("Run this validation as a non-root user.")
    deployments = []
    with tempfile.TemporaryDirectory(prefix="homelab-container-check-") as temporary:
        try:
            source = Deployment(Path(temporary) / "source", args.image); deployments.append(source)
            source.start(); container = source.verify_isolation()
            assert source.request("/api/v1/overview")[0] == 401
            status, page = source.request("/")
            assert status == 200
            assets = re.findall(r'(?:src|href)="(/assets/[^"]+)"', page.decode())
            assert any(path.endswith(".js") for path in assets) and any(path.endswith(".css") for path in assets)
            for path in assets:
                asset_status, content = source.request(path)
                assert asset_status == 200 and content and not content.lstrip().startswith(b"<!doctype html"), "Bundled asset must be served, not the SPA fallback"
            print("PASS: production React entrypoint and referenced JavaScript/CSS assets", flush=True)
            password = secrets.token_urlsafe(24)
            credentials = {"username":"container-validation", "password":password, "setupToken":(source.folder / "secrets/setup-token").read_text()}
            assert source.request("/api/v1/setup", credentials)[0] == 200
            assert source.request("/api/v1/login", credentials)[0] == 200
            assert len(source.cookies) > 0 and all(cookie.secure for cookie in source.cookies)
            plan = {"title":"Container restore fixture", "reason":"Synthetic validation; no hardware purchase", "status":"planned", "currency":"USD", "budget":123, "owner":"validation"}
            assert source.request("/api/v1/plans", plan)[0] == 200
            status, overview = source.request("/api/v1/overview")
            assert status == 200 and overview["connections"] == []
            assert any(p["title"] == plan["title"] for p in overview["plans"])
            # Exercise recovery revision/evidence preservation through the actual production host.
            status, policy = source.request("/api/v1/recovery", {"asset":"Container recovery fixture", "destination":"Synthetic offline copy", "scope":"fixture photos", "destinationCapacity":"8 TiB total; 3 TiB free, offline fixture", "restoreRequirements":"Configuration, permissions and application state fixture"})
            assert status == 200
            recovery_path = "/api/v1/recovery/" + policy["id"]
            now = datetime.now(timezone.utc)
            timestamp = lambda value: value.isoformat().replace("+00:00", "Z")
            run = {"policyRevision":policy["revision"], "attemptedUtc":timestamp(now - timedelta(hours=1)),
                   "recoveryPointUtc":timestamp(now - timedelta(hours=2)), "reviewByUtc":timestamp(now + timedelta(days=1)),
                   "outcome":"success", "destinationConfirmed":True, "evidence":"Synthetic destination manifest"}
            assert source.request(recovery_path + "/runs", run)[0] == 200
            exercise = {"policyRevision":policy["revision"], "testedUtc":run["attemptedUtc"], "recoveryPointUtc":run["recoveryPointUtc"],
                        "reviewByUtc":run["reviewByUtc"], "scope":policy["scope"], "checks":"Synthetic content check", "result":"passed",
                        "durationMinutes":15, "evidence":"Synthetic restore log"}
            assert source.request(recovery_path + "/exercises", exercise)[0] == 200
            before_revision = source.request("/api/v1/recovery")[1][0]
            assert before_revision["assessment"]["recoveryPoint"] == "within RPO"
            assert before_revision["assessment"]["rto"] == "demonstrated within target"
            policy["scope"] = "fixture photos and documents"
            status, revised_policy = source.request(recovery_path, policy, "PUT")
            assert status == 200 and revised_policy["revision"] != policy["revision"]
            assert source.request(recovery_path + "/runs", run)[0] == 409
            assert source.request(recovery_path + "/exercises", exercise)[0] == 409
            status, asset = source.request("/api/v1/inventory/assets", {"name":"Container spare fixture", "type":"spare", "quantity":2,
                                              "estimatedUnitCost":150, "currency":"USD", "compatibilityNotes":"Unverified synthetic fixture"})
            assert status == 200
            print("PASS: production recovery evidence, revision conflicts and local spare inventory", flush=True)
            print("PASS: production HTTPS, owner authentication, non-root isolation and persisted plan", flush=True)
            print("Fixture memory snapshot: " + command(["docker", "stats", "--no-stream", "--format", "{{.MemUsage}}", container]) + " (not a representative pilot)", flush=True)
            settings = source.request("/api/v1/settings")[1]["settings"]
            settings.update({"temperatureReviewCelsius":45, "temperaturePolicyReason":"Synthetic local policy for restore validation", "readDatasets":True})
            assert source.request("/api/v1/settings", settings, "PUT")[0] == 200
            nas_id, disk_id, other_disk_id = seed_monitoring_evidence(source)
            acquisition_path = f"/api/v1/disks/{disk_id}/acquisition"
            status, original_acquisition = source.request(acquisition_path, {"revision":None, "installedUtc":"2025-01-01T00:00:00Z", "installationConfidence":"estimated", "installationEvidence":"Synthetic installation log", "purchaseCost":219.99, "currency":"USD", "costConfidence":"documented", "purchaseEvidence":"Synthetic receipt reference"}, "PUT")
            assert status == 200 and original_acquisition["purchaseCost"] == 219.99
            status, redacted_acquisition = source.request("/api/v1/inventory/export?redacted=true")
            assert status == 200 and redacted_acquisition["driveAcquisitions"][0]["purchaseEvidence"] is None
            original_local_evidence = record_local_drive_evidence(source, disk_id, asset)
            original_smart = record_smart_evidence(source, disk_id)
            original_identity = record_identity_and_paths(source,nas_id,disk_id,other_disk_id)
            original_dataset_renames = record_dataset_rename(source,nas_id)
            print("PASS: production hardware-path supersession, identity merge/reversal and revoked discovery evidence",flush=True)
            print("PASS: production SMART device/self-test evidence and audited manual resolution",flush=True)
            print("PASS: production maintenance corrections, spare supersession, idempotency and export redaction", flush=True)
            original_activity_id, original_activity = retained_activity(source)
            assert original_activity
            original_runs = retained_recovery(source, policy["id"], "runs")
            original_exercises = retained_recovery(source, policy["id"], "exercises")
            original_monitoring = verify_monitoring_evidence(source, nas_id)
            print("PASS: paused synthetic dataset/scan evidence and configured local temperature policy", flush=True)
            # Synthetic credentials only. Leave disabled/untested; never contact Telegram.
            telegram_fixture = {"kind":"telegram", "botToken":"123456:abcdefghijklmnopqrstuvwxyz", "chatId":"-100123456", "enabled":False, "maskIdentifiers":True}
            assert source.request("/api/v1/notifications", telegram_fixture, "PUT")[0] == 200
            public_channel = source.request("/api/v1/notifications")[1]["channel"]
            assert public_channel["kind"] == "telegram" and public_channel["enabled"] is False and public_channel["testedUtc"] is None
            assert telegram_fixture["botToken"] not in json.dumps(public_channel) and telegram_fixture["chatId"] not in json.dumps(public_channel)
            status, backup = source.request("/api/v1/backups", {})
            assert status == 200
            archives = list((source.folder / "backups").glob("*.zip")); assert len(archives) == 1
            # Check the database actually included in the archive, not just the live model.
            archived_db = Path(temporary) / "archived-evidence.db"
            with zipfile.ZipFile(archives[0]) as archive:
                archived_db.write_bytes(archive.read("homelab.db"))
            with sqlite3.connect(f"file:{archived_db}?mode=ro", uri=True) as database:
                assert database.execute("PRAGMA integrity_check").fetchone()[0] == "ok"
                archived_channel = database.execute("SELECT Kind, ProtectedUrl, Enabled, TestedUtc FROM NotificationChannels").fetchone()
                assert archived_channel[0] == "telegram" and archived_channel[1] and archived_channel[2:] == (0, None)
                assert telegram_fixture["botToken"] not in archived_channel[1] and telegram_fixture["chatId"] not in archived_channel[1]
                snapshot_columns = {row[1] for row in database.execute("PRAGMA table_info(PoolSnapshots)")}
                assert {"ScanStartedUtc", "ScanEndedUtc", "ScanPausedUtc", "ScanRemainingSeconds", "StatusCode", "VdevEvidenceJson", "SourceContextJson"} <= snapshot_columns
                daily_columns = {row[1] for row in database.execute("PRAGMA table_info(PoolDailySummaries)")}
                assert "StatusCode" in daily_columns
                migrations = [row[0] for row in database.execute('SELECT MigrationId FROM "__EFMigrationsHistory"')]
                assert any(row.endswith("_DailyPoolStatusCode") for row in migrations)
                scan_columns = {row[1] for row in database.execute("PRAGMA table_info(PoolScans)")}
                assert "ScanErrorsPeak" in scan_columns
                for suffix in ("_LocalTemperaturePolicy", "_PoolScanHistory", "_ScanErrorPeaks", "_DatasetUsage", "_DriveMaintenance", "_SpareCompatibilityReviews", "_SelfTestEvidence", "_VdevSnapshotEvidence", "_PoolSourceContext", "_DriveHardwarePaths", "_ReversibleIdentityHistory", "_StableDiscoveryAssignments", "_ObservationHistoryPaging", "_RecoveryEvidenceIntegrity", "_NotificationAttemptIntegrity", "_CollectionSourceIntegrity", "_DriveRecordIntegrity", "_CoreIdentityIntegrity", "_ReviewedIdentityIntegrity", "_DeliveryFindingIntegrity", "_DatasetInventoryHistory", "_DatasetRenameReviews", "_RecoveryPlanningContext", "_DriveAcquisitionNotes"):
                    assert any(row.endswith(suffix) for row in migrations)
                assert database.execute("SELECT COUNT(*) FROM Connections").fetchone()[0] == 1
                assert database.execute("SELECT Enabled FROM Connections").fetchone()[0] == 0
                assert database.execute("SELECT ScanErrors, ScanErrorsPeak FROM PoolScans").fetchone() == (0, 7)
                assert database.execute("SELECT COUNT(*) FROM CollectionRuns").fetchone()[0] == 0
                assert database.execute("PRAGMA foreign_key_check").fetchall() == []
                assert database.execute("SELECT COUNT(*) FROM Maintenance WHERE VoidedUtc IS NOT NULL").fetchone()[0] == 1
                assert database.execute("SELECT COUNT(*) FROM SpareReviews").fetchone()[0] == 2
                assert database.execute("SELECT COUNT(*) FROM SelfTestEvidence").fetchone()[0] == 1
                assert database.execute("SELECT COUNT(*) FROM SmartReports").fetchone()[0] == 1
            print("PASS: backup integrity and current scan/integrity evidence schema", flush=True)
            keys = list((source.folder / "data").rglob("key-*.xml")); assert keys
            assert all("encryptedSecret" in path.read_text() for path in keys), "Data Protection keys must be encrypted"
            def probe_service(expected):
                result = subprocess.run([sys.executable, str(ROOT / "scripts/check-service.py"), source.base + "/health/ready",
                    "--ca-file", str(source.folder / "secrets/tls.crt"), "--timeout", "3"], capture_output=True, text=True, timeout=5)
                report = json.loads(result.stdout)
                assert result.returncode == (0 if expected == "ready" else 1), report
                assert report["status"] == expected, report
                return report
            probe_service("ready")
            command(source.compose + ["stop", "homelab"])
            stopped = probe_service("not_ready")
            command(source.compose + ["start", "homelab"]); source.ready()
            probe_service("ready")
            print("PASS: independent-process HTTPS checker detects stopped service and recovery (same host only)", flush=True)
            assert source.request("/api/v1/overview")[0] == 200, "Protected login cookie must survive restart"
            print("PASS: application backup, encrypted key ring and authenticated restart", flush=True)
            restored = Deployment(Path(temporary) / "restored", args.image); deployments.append(restored)
            (restored.folder / "data").rmdir()  # Empty disposable preparation directory only.
            command([sys.executable, str(ROOT / "scripts/restore-backup.py"), str(archives[0]), str(restored.folder / "data")])
            restored.start(); restored.verify_isolation()
            assert restored.request("/api/v1/overview")[0] == 401
            assert restored.request("/api/v1/login", credentials)[0] == 200
            status, restored_overview = restored.request("/api/v1/overview")
            assert status == 200 and len(restored_overview["connections"]) == 1
            assert verify_monitoring_evidence(restored, nas_id) == original_monitoring
            assert restored.request(f"/api/v1/connections/{nas_id}/dataset-renames")[1]["items"] == original_dataset_renames
            print("PASS: exact reversed dataset rename review and both source inventories restored",flush=True)
            status, restored_acquisition = restored.request(acquisition_path)
            assert status == 200 and restored_acquisition["record"] == original_acquisition
            print("PASS: exact installation and purchase evidence restored with cost/currency/confidence", flush=True)
            assert verify_local_drive_evidence(restored, disk_id, asset) == original_local_evidence
            assert verify_identity_and_paths(restored,nas_id,disk_id)==original_identity
            print("PASS: exact hardware paths, identity grouping/reversal and discovery revocation restored",flush=True)
            verify_restored_smart_evidence(restored,disk_id,original_smart)
            print("PASS: restored SMART evidence, resolution and durable repeated-failure suppression",flush=True)
            print("PASS: exact maintenance correction and superseded spare evidence retained through restore", flush=True)
            with sqlite3.connect(f"file:{restored.folder / 'data/homelab.db'}?mode=ro", uri=True) as restored_db:
                assert restored_db.execute("SELECT ProtectedApiKey, Enabled FROM Connections").fetchone() == ("", 0)
                assert restored_db.execute("SELECT COUNT(*) FROM CollectionRuns").fetchone()[0] == 0
                assert restored_db.execute("SELECT Kind, ProtectedUrl, Enabled, TestedUtc FROM NotificationChannels").fetchone() == ("telegram", "", 0, None)
                assert restored_db.execute("SELECT COUNT(*) FROM DeliveryAttempts").fetchone()[0] == 0
            print("PASS: Telegram credentials encrypted in backup and cleared on restore; no outbound attempt", flush=True)
            print("PASS: dataset null/zero values, scan-error peaks and policy retained; restored credentials cleared", flush=True)
            assert any(p["title"] == plan["title"] and p["budget"] == 123 for p in restored_overview["plans"])
            assert any(a["action"] == "Backup restored" for a in restored_overview["activity"])
            assert restored.request("/api/v1/settings")[1]["settings"]["backupsEnabled"] is False
            assert retained_activity(restored, original_activity_id)[1] == original_activity
            assert retained_recovery(restored, policy["id"], "runs") == original_runs
            assert retained_recovery(restored, policy["id"], "exercises") == original_exercises
            print("PASS: exact retained activity and recovery history through production HTTPS and restore", flush=True)
            restored_recovery = restored.request("/api/v1/recovery")[1][0]
            assert restored_recovery["policy"]["revision"] == revised_policy["revision"]
            assert restored_recovery["policy"]["scope"] == "fixture photos and documents"
            assert restored_recovery["policy"]["destinationCapacity"] == "8 TiB total; 3 TiB free, offline fixture"
            assert restored_recovery["policy"]["restoreRequirements"] == "Configuration, permissions and application state fixture"
            assert len(restored_recovery["runs"]) == 1 and len(restored_recovery["exercises"]) == 1
            assert restored_recovery["runs"][0]["evidence"] == run["evidence"]
            assert restored_recovery["exercises"][0]["evidence"] == exercise["evidence"]
            assert restored_recovery["assessment"]["recoveryPoint"] == "unverified"
            assert restored_recovery["assessment"]["restore"] == "not tested"
            restored_asset = restored.request("/api/v1/inventory")[1]["assets"][0]
            assert restored_asset["id"] == asset["id"] and restored_asset["quantity"] == 2 and restored_asset["estimatedUnitCost"] == 150
            print("PASS: isolated restore with new certificates/key ring, original owner login and retained evidence", flush=True)
            print("Same-host exercise only; independent-host restore and seven-day pilot remain release gates.", flush=True)
        finally:
            for deployment in reversed(deployments): deployment.stop()

if __name__ == "__main__":
    main()
