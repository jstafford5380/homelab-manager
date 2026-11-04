import {useEffect,useRef,useState,type ReactNode} from "react";
import {Link} from "react-router";
import {TriangleAlert} from "lucide-react";
import {Card} from "@/components/ui/card";
import {Button} from "@/components/ui/button";
import {Input} from "@/components/ui/input";
import {Label} from "@/components/ui/label";
import {SettingsSaveBar} from "./SettingsSaveBar";
import {useDisplayFormat} from "../../lib/displayPreferences";

import {connectionStatus,type Connection} from "./connectionModel";
function Status({ connection }: { connection: Connection }) {
  const { text, tone, icon: Icon } = connectionStatus(connection);
  return <span className={`ov-pill tone-${tone}`}><Icon size={14} aria-hidden="true" />{text}</span>;
}

export function ConnectionSettings({ id, connections, error, onConnections, onDirty }: {
  id?: string;
  connections: Connection[];
  error: string;
  onConnections: (connections: Connection[]) => void;
  onDirty: (dirty: boolean) => void;
}) {
  const { time } = useDisplayFormat();
  const connection = connections.find(connection => connection.id === id);
  if (error) return <p role="alert">{error}</p>;
  if (id) return connection
    ? <ConnectionDetail key={id} connection={connection} onDirty={onDirty} onConnections={onConnections} />
    : <p role="status">Connection not found.</p>;
  return <Card className="settings-group">
    {connections.length ? connections.map(connection => <div key={connection.id} className="settings-row settings-connection">
      <div><h3>{connection.name} <Status connection={connection} /></h3>
        <p className="muted settings-meta">{connection.connectHost ? `Via tunnel ${connection.connectHost}:${connection.connectPort}` : connection.apiUri}
          {" · "}{connection.status === "Collection paused by owner" ? "Paused by you" : connection.lastSuccessUtc ? `Last read ${time(connection.lastSuccessUtc)}` : "No successful read recorded"}
          {connection.enabled && ` · Every ${connection.pollSeconds} seconds`}</p></div>
      <Button asChild variant="outline"><Link to={`/settings/connections/${connection.id}`}
        aria-label={`${connectionStatus(connection).attention ? "Review" : "Manage"} ${connection.name}`}>{connectionStatus(connection).attention ? "Review" : "Manage"}</Link></Button>
    </div>) : <div className="empty"><h3>No systems connected yet</h3>
      <p>Connect a TrueNAS system directly or through an SSH tunnel you already run. Until then, every health status stays unknown.</p>
    </div>}
  </Card>;
}

const fields = [
  ["name", "Name"], ["apiUri", "Logical NAS URI"], ["connectHost", "Tunnel host (optional)"],
  ["connectPort", "Tunnel port (optional)"], ["pollSeconds", "Collection interval (seconds)"],
  ["enabled", "Collection enabled"], ["trustedCaPem", "Trusted public CA/self-signed certificate (PEM)"],
  ["certificateSha256", "Enrolled certificate SHA-256"],
] as const;
type Field = typeof fields[number][0];

function ConnectionDetail({ connection, onDirty, onConnections }: {
  connection: Connection;
  onDirty: (dirty: boolean) => void;
  onConnections: (connections: Connection[]) => void;
}) {
  const { time } = useDisplayFormat();
  const [editing, setEditing] = useState(connection);
  const [draft, setDraft] = useState(connection);
  const [message, setMessage] = useState("");
  const [success, setSuccess] = useState("");
  const [conflict, setConflict] = useState(false);
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [busy, setBusy] = useState(false);
  const [actionDirty, setActionDirty] = useState(false);
  const alert = useRef<HTMLParagraphElement>(null);
  const changes = fields.filter(([key]) => draft[key] !== editing[key]).length;
  useEffect(() => { onDirty(changes > 0 || actionDirty); }, [changes, actionDirty, onDirty]);
  useEffect(() => () => onDirty(false), [onDirty]);
  useEffect(() => { document.getElementById("connection-" + Object.keys(errors)[0])?.focus(); }, [errors]);

  useEffect(() => { if (message && changes === 0) alert.current?.focus(); }, [message, changes]);

  async function reload() {
    const response = await fetch("/api/v1/connections");
    if (!response.ok) throw new Error("Could not reload connections.");
    const rows: Connection[] = await response.json();
    const latest = rows.find(row => row.id === editing.id);
    if (!latest) throw new Error("This connection is no longer available.");
    onConnections(rows);
    setEditing(latest);
    setDraft(latest);
    setActionDirty(false);
    setConflict(false);
    setErrors({});
  }
  async function save(path: string, body: unknown) {
    setBusy(true);
    setMessage("");
    setSuccess("");
    setErrors({});
    try {
      const response = await fetch("/api/v1/connections/" + path, {
        method: "PUT",
        headers: { "Content-Type": "application/json", "X-Homelab-Request": "1" },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        setConflict(response.status === 409);
        const error = await response.json().catch(() => ({}));
        if (error.errors) setErrors(error.errors);
        throw new Error(error.errors ? "Not saved. Fix the highlighted field and try again." : error.error ?? "Could not update connection.");
      }
      setSuccess("Connection updated. Validation occurs on the next collection.");
      await reload();
      return true;
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error));
      return false;
    } finally { setBusy(false); }
  }
  function row(key: Field, label: string): ReactNode {
    const attributes = { id: "connection-" + key, disabled: busy, "aria-invalid": !!errors[key],
      "aria-describedby": errors[key] ? "connection-error-" + key : undefined };
    const value = draft[key];
    const previous = editing[key];
    return <div className="settings-row" key={key}><div><Label htmlFor={attributes.id}>{label}</Label>
      {key === "apiUri" && <p className="muted">TLS is always validated against this hostname.</p>}
      {key === "trustedCaPem" && <p className="muted">Leave blank to use the operating system’s trust store.</p>}
      {key === "enabled" && <p className="muted">Pausing stops future collection. Any snapshot from a read already in progress is discarded.</p>}</div>
      <div>
        {key === "enabled" ? <input {...attributes} type="checkbox" role="switch" checked={draft.enabled}
          onChange={event => setDraft({ ...draft, enabled: event.target.checked })} /> :
          key === "trustedCaPem" ? <textarea {...attributes} rows={4} value={draft.trustedCaPem ?? ""}
            onChange={event => setDraft({ ...draft, trustedCaPem: event.target.value || null })} /> :
          <Input {...attributes} type={key === "connectPort" || key === "pollSeconds" ? "number" : "text"}
            value={typeof value === "boolean" ? "" : value ?? ""} onChange={event => {
              const value = event.target.value;
              setDraft({ ...draft, [key]: key === "pollSeconds" ? Number(value) : key === "connectPort" ? value ? Number(value) : null : value });
            }} />}
        {previous !== value && <p className="muted settings-meta">Was {key === "trustedCaPem" ? previous ? "a configured certificate" : "unset" :
          typeof previous === "boolean" ? previous ? "On" : "Off" : previous || "unset"}</p>}
        {errors[key] && <p className="error" id={"connection-error-" + key}>{errors[key].join(" ")}</p>}
      </div>
    </div>;
  }
  const reloadAction = () => {
    setBusy(true);
    void reload().then(() => setMessage("")).catch(error => setMessage(error.message)).finally(() => setBusy(false));
  };
  return <>
    <div><Status connection={editing} /> <span className="muted">{editing.status}</span></div>
    {editing.systemSerial &&
      editing.observedSystemSerial &&
      editing.systemSerial !== editing.observedSystemSerial && (
        <>

          <Card className="settings-group settings-identity-review" key={editing.configurationRevision}><form onChange={() => setActionDirty(true)}
            onSubmit={(e) => {
              e.preventDefault();
              const f = Object.fromEntries(
                new FormData(e.currentTarget),
              );
              void save(editing.id + "/identity", { ...f, configurationRevision: editing.configurationRevision });
            }}
          >
            <h3><TriangleAlert size={18} aria-hidden="true" /> This system reported a different serial</h3>
            <p>Collection is paused so evidence from two machines isn’t mixed. Historical evidence is kept either way.</p>
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
            <Button variant="outline" disabled={busy || changes > 0 || conflict}>
              Accept new baseline & resume
            </Button>
          </form></Card>
        </>
      )}
    {editing.systemSerial && !editing.observedSystemSerial && (
      <p className="muted">
        {editing.systemIdentityState === "awaiting validation"
          ? "The endpoint or certificate trust changed. After reviewing connection settings, resume collection to obtain a new authenticated identity read before accepting a baseline change. Historical evidence is retained."
          : "The saved system serial is currently missing. Restore the expected endpoint/identity before resuming; a missing serial cannot be accepted as a new baseline."}
      </p>
    )}

    <form noValidate className="settings-form" onSubmit={event => {
      event.preventDefault();
      if (!changes || busy || conflict) return;
      void save(editing.id, { ...draft, connectHost: draft.connectHost || null, certificateSha256: draft.certificateSha256 || null });
    }}>
      <Card className="settings-group">
        <div className="settings-group-heading"><h3>Connection</h3></div>
        {fields.slice(0, 6).map(([key, label]) => row(key, label))}
      </Card>
      <Card className="settings-group">
        <div className="settings-group-heading"><h3>Certificate trust</h3></div>
        {fields.slice(6).map(([key, label]) => row(key, label))}
        <div className="settings-row"><details><summary>How this works</summary>
          <p>Private-CA trust and SHA-256 pinning always validate the NAS hostname. A successful first trusted connection records its fingerprint. For a planned change, verify the new fingerprint independently before entering it. Clearing the fingerprint enrolls the next certificate that passes trust and hostname checks.</p>
        </details></div>
      </Card>
      {changes > 0 && <SettingsSaveBar section="Connection" count={changes} error={message} busy={busy}
        onDiscard={() => { setDraft(editing); setErrors({}); if (!conflict) setMessage(""); }}
        onReload={conflict ? reloadAction : undefined} />}
    </form>
    <div className="settings-meta"><strong>System identity: {editing.systemIdentityState}</strong><p>Saved serial: {editing.systemSerial || "No usable serial recorded"} · Last reported: {editing.observedSystemSerial || "Unavailable"}</p>
      <p className="muted">Checked: {editing.systemIdentityCheckedUtc ? time(editing.systemIdentityCheckedUtc) : "Never"}. A DMI serial is supplementary evidence, not a guaranteed unique hardware identifier.</p>
    </div>
    <Card className="settings-group" key={"key-" + editing.configurationRevision}><form onChange={() => setActionDirty(true)}
      onSubmit={(e) => {
        e.preventDefault();
        const form = e.currentTarget;
        const f = new FormData(form);
        void save(editing.id + "/credentials", {
          apiKey: f.get("apiKey"),
          configurationRevision: editing.configurationRevision,
        }).then(updated => { if (updated) form.reset(); });
      }}
    >
      <h3>API key</h3>
      <p className="muted">Stored encrypted. It’s never shown again after saving.</p>
      {editing.apiKeyReplacedUtc && <p className="muted">Last replaced {time(editing.apiKeyReplacedUtc)}</p>}
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
      <Button variant="outline" disabled={busy || changes > 0 || conflict}>
        Replace API key & resume
      </Button>
    </form></Card>

    {changes === 0 && message && <p ref={alert} tabIndex={-1} role="alert" className="error">{message}</p>}
    {changes === 0 && conflict && <Button variant="outline" disabled={busy} onClick={reloadAction}>Reload saved connection</Button>}
    {success && <p role="status">{success}</p>}
  </>;
}
