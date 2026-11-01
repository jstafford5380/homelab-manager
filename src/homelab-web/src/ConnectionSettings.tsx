import { useEffect, useRef, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
type Connection = {
  id: string;
  configurationRevision: string;
  name: string;
  apiUri: string;
  connectHost: string | null;
  connectPort: number | null;
  pollSeconds: number;
  enabled: boolean;
  trustedCaPem: string | null;
  certificateSha256: string | null;
  status: string;
  systemSerial: string | null;
  observedSystemSerial: string | null;
  systemIdentityState: string;
  systemIdentityCheckedUtc: string | null;
};
export function ConnectionSettings({ timezone }: { timezone: string }) {
  const [connections, setConnections] = useState<Connection[]>([]);
  const [editing, setEditing] = useState<Connection | null>(null);
  const [message, setMessage] = useState("");
  const messageRef = useRef<HTMLParagraphElement>(null);
  useEffect(() => {
    if (message && editing) messageRef.current?.focus();
  }, [message, editing]);
  const [conflict, setConflict] = useState(false);
  const [busy, setBusy] = useState(false);
  async function load() {
    const response = await fetch("/api/v1/connections");
    if (!response.ok) throw new Error("Could not load connections");
    const data: Connection[] = await response.json();
    setConnections(data);
    return data;
  }
  useEffect(() => {
    void load().catch((e) => setMessage(String(e)));
  }, []);
  async function save(path: string, body: unknown) {
    setBusy(true);
    setMessage("");
    setConflict(false);
    try {
      const response = await fetch("/api/v1/connections/" + path, {
        method: "PUT",
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        setConflict(response.status === 409);
        const error = await response.json().catch(() => ({}));
        throw new Error(error.error || "Could not update connection");
      }
      await load();
      setEditing(null);
      setMessage(
        "Connection updated. Validation occurs on the next collection.",
      );
    } catch (e) {
      setMessage(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <section style={{ marginTop: 24 }}>
      <h2>Manage connection identity & certificate trust</h2>
      <p className="muted">
        Pausing stops future collection attempts; a read already in progress may
        finish but its snapshot will be discarded. Private-CA trust and SHA-256
        pinning never disable hostname validation.
      </p>
      {connections.map((c) => (
        <Card key={c.id} className="list-card" style={{ marginTop: 12 }}>
          <div className="row">
            <div>
              <h3>{c.name}</h3>
              <small>
                {c.status} · {c.pollSeconds}s interval
              </small>
            </div>
            <Button variant="outline" onClick={() => { setMessage(""); setConflict(false); setEditing(c); }}>
              Edit connection
            </Button>
            <Button
              variant="outline"
              disabled={busy}
              onClick={() => void save(c.id, { ...c, enabled: !c.enabled })}
            >
              {c.enabled ? "Pause collection" : "Resume collection"}
            </Button>
          </div>
          <div className="connection-identity">
            <strong>
              System identity: {c.systemIdentityState || "unavailable"}
            </strong>
            <p>
              Saved serial: {c.systemSerial || "No usable serial recorded"} ·
              Last reported: {c.observedSystemSerial || "Unavailable"}
            </p>
            <small>
              Checked:{" "}
              {c.systemIdentityCheckedUtc
                ? new Date(c.systemIdentityCheckedUtc).toLocaleString(
                    undefined,
                    { timeZone: timezone },
                  )
                : "Never"}
              . A DMI serial is supplementary evidence, not a guaranteed unique
              hardware identifier.
            </small>
          </div>
        </Card>
      ))}
      {message && !editing && <p role="status">{message}</p>}
      {conflict && !editing && <Button variant="outline" disabled={busy} onClick={() => {
        void load().then(() => { setConflict(false); setMessage("Connections reloaded. Review the latest state before retrying."); })
          .catch(error => setMessage(error instanceof Error ? error.message : "Could not reload connections."));
      }}>Reload connections</Button>}
      <Dialog
        open={!!editing}
        onOpenChange={(open) => {
          if (!open) setEditing(null);
        }}
      >
        <DialogContent
          key={editing?.configurationRevision}
          style={{
            maxHeight: "85vh",
            overflow: "auto",
            maxWidth: "min(680px, calc(100vw - 2rem))",
          }}
        >
          <DialogHeader>
            <DialogTitle>Connection, tunnel & trust</DialogTitle>
            <DialogDescription>
              These settings change how Homelab Control reads the NAS. They do
              not modify NAS configuration.
            </DialogDescription>
          </DialogHeader>
          {editing && (
            <>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  const f = Object.fromEntries(new FormData(e.currentTarget));
                  void save(editing.id, {
                    ...f,
                    configurationRevision: editing.configurationRevision,
                    enabled: f.enabled === "on",
                    pollSeconds: Number(f.pollSeconds),
                    connectHost: f.connectHost || null,
                    connectPort: f.connectPort ? Number(f.connectPort) : null,
                    trustedCaPem: f.trustedCaPem || null,
                    certificateSha256: f.certificateSha256 || null,
                  });
                }}
              >
                <div className="settings-grid">
                  {[
                    { name: "name", label: "Name", value: editing.name },
                    {
                      name: "apiUri",
                      label: "Logical NAS URI",
                      value: editing.apiUri,
                    },
                    {
                      name: "connectHost",
                      label: "Tunnel host (optional)",
                      value: editing.connectHost || "",
                    },
                    {
                      name: "connectPort",
                      label: "Tunnel port (optional)",
                      value: editing.connectPort || "",
                      type: "number",
                    },
                    {
                      name: "pollSeconds",
                      label: "Collection interval (30–3600 seconds)",
                      value: editing.pollSeconds,
                      type: "number",
                    },
                  ].map((field) => (
                    <div className="field" key={field.name}>
                      <Label htmlFor={"connection-" + field.name}>
                        {field.label}
                      </Label>
                      <Input
                        id={"connection-" + field.name}
                        name={field.name}
                        defaultValue={field.value}
                        type={field.type || "text"}
                      />
                    </div>
                  ))}
                </div>
                <label className="backup-checkbox">
                  <input
                    type="checkbox"
                    name="enabled"
                    defaultChecked={editing.enabled}
                  />{" "}
                  Collection enabled
                </label>
                <div className="field" style={{ marginTop: 16 }}>
                  <Label htmlFor="trusted-ca">
                    Trusted public CA/self-signed certificate (PEM)
                  </Label>
                  <textarea
                    id="trusted-ca"
                    name="trustedCaPem"
                    rows={5}
                    defaultValue={editing.trustedCaPem || ""}
                  />
                </div>
                <div className="field">
                  <Label htmlFor="certificate-pin">
                    Enrolled certificate SHA-256
                  </Label>
                  <Input
                    id="certificate-pin"
                    name="certificateSha256"
                    defaultValue={editing.certificateSha256 || ""}
                  />
                  <p className="muted">
                    A successful first trusted connection records this
                    fingerprint. For a planned certificate change, verify the
                    new fingerprint independently and enter it here. Clearing it
                    explicitly reenrolls the next certificate that passes trust
                    and hostname checks.
                  </p>
                </div>
                <Button disabled={busy}>Save connection</Button>
              </form>
              {editing.systemSerial &&
                editing.observedSystemSerial &&
                editing.systemSerial !== editing.observedSystemSerial && (
                  <>
                    <hr />
                    <form
                      onSubmit={(e) => {
                        e.preventDefault();
                        const f = Object.fromEntries(
                          new FormData(e.currentTarget),
                        );
                        void save(editing.id + "/identity", { ...f, configurationRevision: editing.configurationRevision });
                      }}
                    >
                      <h3>Review a changed system identity</h3>
                      <p className="muted">
                        Saved: {editing.systemSerial}. Newly reported:{" "}
                        {editing.observedSystemSerial}. First verify the
                        physical NAS, endpoint and certificate independently.
                        Accept a new baseline only for a documented hardware
                        change on this same NAS; enroll a separate connection
                        for a different NAS.
                      </p>
                      <div className="field">
                        <Label htmlFor="identity-previous">
                          Retype saved system serial
                        </Label>
                        <Input
                          id="identity-previous"
                          name="previousSerial"
                          required
                          autoComplete="off"
                        />
                      </div>
                      <div className="field">
                        <Label htmlFor="identity-new">
                          Retype newly reported system serial
                        </Label>
                        <Input
                          id="identity-new"
                          name="newSerial"
                          required
                          autoComplete="off"
                        />
                      </div>
                      <div className="field">
                        <Label htmlFor="identity-evidence">
                          Independent verification evidence
                        </Label>
                        <Input
                          id="identity-evidence"
                          name="evidence"
                          required
                          maxLength={4000}
                        />
                      </div>
                      <Button variant="outline" disabled={busy}>
                        Accept identity baseline & resume
                      </Button>
                    </form>
                  </>
                )}
              {editing.systemSerial && !editing.observedSystemSerial && (
                <p className="muted">
                  {editing.systemIdentityState === "awaiting validation"
                    ? "The endpoint or certificate trust changed. After reviewing connection settings, resume collection to obtain a new authenticated identity read before accepting a baseline change. Historical evidence is retained."
                    : "The saved system serial is currently missing. Restore the expected endpoint/identity before resuming; a missing serial cannot be accepted as a new baseline."}
                </p>
              )}
              <hr />
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  const f = new FormData(e.currentTarget);
                  void save(editing.id + "/credentials", {
                    apiKey: f.get("apiKey"),
                    configurationRevision: editing.configurationRevision,
                  });
                }}
              >
                <div className="field">
                  <Label htmlFor="rotated-key">New NAS API key</Label>
                  <Input
                    maxLength={8192}
                    autoComplete="new-password"
                    id="rotated-key"
                    name="apiKey"
                    type="password"
                    required
                  />
                </div>
                <Button variant="outline" disabled={busy}>
                  Rotate credential & resume
                </Button>
              </form>
              {message && (
                <p className="error" role="alert" tabIndex={-1} ref={messageRef}>
                  {message}
                </p>
              )}
              {conflict && <>
                <p className="muted">Reloading discards this dialog’s unsaved edits and clears the entered API key.</p>
                <Button variant="outline" disabled={busy} onClick={async () => {
                  setBusy(true);
                  try {
                    const latest = (await load()).find(c => c.id === editing.id);
                    if (!latest) throw new Error("This connection is no longer available.");
                    setEditing(latest); setConflict(false); setMessage("");
                  } catch (error) { setMessage(error instanceof Error ? error.message : "Could not reload connection."); }
                  finally { setBusy(false); }
                }}>Reload saved connection</Button>
              </>}
            </>
          )}
        </DialogContent>
      </Dialog>
    </section>
  );
}
