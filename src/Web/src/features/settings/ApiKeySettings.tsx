import {useEffect,useRef,useState} from "react";
import {Card} from "@/components/ui/card";
import {Button} from "@/components/ui/button";
import {Input} from "@/components/ui/input";
import {Dialog,DialogContent,DialogDescription,DialogHeader,DialogTitle} from "@/components/ui/dialog";
import {SelectField,SelectOption} from "@/components/SelectField";
import {useDisplayFormat} from "../../lib/displayPreferences";

type ApiKey = { id: string; name: string; prefix: string; access: "read" | "read-write"; createdUtc: string;
  expiresUtc: string; lastUsedUtc: string | null; revokedUtc: string | null };
type IssuedKey = { key: ApiKey; secret: string };

async function keyRequest(path = "", options?: RequestInit) {
  const response = await fetch("/api/v1/api-keys" + path, { ...options, signal: options?.signal ?? AbortSignal.timeout(15_000),
    headers: { "Content-Type": "application/json", "X-Homelab-Request": "1", ...options?.headers } });
  if (!response.ok) {
    const result = await response.json().catch(() => null);
    throw new Error(result?.error ?? `API key request failed (HTTP ${response.status}).`);
  }
  return response;
}

export function ApiKeySettings({ onDirty }: { onDirty: (dirty: boolean) => void }) {
  const { time } = useDisplayFormat();
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => { const timer = window.setInterval(() => setNow(Date.now()), 30_000); return () => window.clearInterval(timer); }, []);
  const [keys, setKeys] = useState<ApiKey[] | null>(null);
  const [name, setName] = useState("");
  const [access, setAccess] = useState("read");
  const [days, setDays] = useState("90");
  const [issued, setIssued] = useState<IssuedKey | null>(null);
  const [revoke, setRevoke] = useState<ApiKey | null>(null);
  const [error, setError] = useState("");
  const [loadError, setLoadError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [retry, setRetry] = useState(0);
  const secretInput = useRef<HTMLInputElement>(null);
  useEffect(() => { onDirty(name !== "" || access !== "read" || days !== "90"); }, [name, access, days, onDirty]);
  useEffect(() => () => onDirty(false), [onDirty]);
  useEffect(() => {
    const controller = new AbortController();
    void keyRequest("", { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15_000)]) })
      .then(response => response.json()).then((result: ApiKey[]) => { if (!controller.signal.aborted) { setKeys(result); setLoadError(""); } })
      .catch(error => { if (!controller.signal.aborted) setLoadError(error.message); });
    return () => controller.abort();
  }, [retry]);
  async function issue() {
    setBusy(true); setError(""); setMessage("");
    try {
      const response = await keyRequest("", { method: "POST", body: JSON.stringify({ name, access, expiresInDays: Number(days) }) });
      const result: IssuedKey = await response.json();
      setIssued(result);
      setKeys(current => [result.key, ...(current ?? [])]);
      setName(""); setAccess("read"); setDays("90");
    } catch (error) { setError(error instanceof Error ? error.message : String(error)); }
    finally { setBusy(false); }
  }
  async function revokeKey() {
    if (!revoke) return;
    setBusy(true); setError("");
    try {
      await keyRequest("/" + revoke.id, { method: "DELETE" });
      setKeys(current => current?.map(key => key.id === revoke.id ? { ...key, revokedUtc: new Date().toISOString() } : key) ?? null);
      setMessage(`Revoked ${revoke.name}.`); setRevoke(null);
    } catch (error) { setError(error instanceof Error ? error.message : String(error)); }
    finally { setBusy(false); }
  }
  async function copy() {
    if (!issued) return;
    try { await navigator.clipboard.writeText(issued.secret); setMessage("API key copied."); }
    catch { secretInput.current?.focus(); secretInput.current?.select(); setMessage("Select and copy the API key from the field."); }
  }
  return <>
    {error && <p role="alert" className="error">{error}</p>}
    {message && <p role="status">{message}</p>}
    <Card className="settings-group api-key-issue">
      <div className="settings-group-heading"><div><h3>Issue an API key</h3><p className="muted">One key per integration. Give it only the access it needs.</p></div></div>
      <form className="api-key-form" onSubmit={event => { event.preventDefault(); void issue(); }}>
        <div className="api-key-fields">
          <div className="api-key-field api-key-name"><label htmlFor="api-key-name">Key name</label><Input id="api-key-name" value={name} maxLength={100} required
            placeholder="e.g. Desktop MCP client" onChange={event => setName(event.target.value)} disabled={busy} /></div>
          <div className="api-key-field"><label htmlFor="api-key-access">Access</label><SelectField id="api-key-access" value={access} onValueChange={setAccess} disabled={busy} aria-describedby="api-key-access-summary">
            <SelectOption value="read">Read-only</SelectOption><SelectOption value="read-write">Read and write</SelectOption>
          </SelectField></div>
          <div className="api-key-field"><label htmlFor="api-key-expiry">Expires after</label><SelectField id="api-key-expiry" value={days} onValueChange={setDays} disabled={busy}>
            <SelectOption value="30">30 days</SelectOption><SelectOption value="90">90 days</SelectOption><SelectOption value="365">365 days</SelectOption>
          </SelectField></div>
        </div>
        <div className="api-key-permissions">
          <p id="api-key-access-summary" className="muted">{access === "read" ? "Can view application records. Cannot make changes." : "Can view and update application records, and request read-only collections."}</p>
          <details><summary>Access details</summary><div>
            <p>Read-only keys can view monitoring, inventory, planning, and recovery records. Read and write keys can also update those records and request supported collections.</p>
            <p>Keys cannot manage settings, credentials, notifications, backups, or other keys. NAS configuration is never changed.</p>
          </div></details>
        </div>
        <div className="api-key-form-footer"><p className="muted">The secret is shown only once. Store it somewhere secure.</p>
          <Button type="submit" disabled={busy || !name.trim() || keys === null}>{busy ? "Working…" : "Issue API key"}</Button>
        </div>
      </form>
    </Card>
    <Card className="settings-group">
      <div className="settings-group-heading"><h3>Issued keys</h3><p className="muted">Last used updates at most once every five minutes.</p></div>
      {loadError && <div className="settings-row"><p role="alert">{loadError}</p><Button variant="outline" onClick={() => setRetry(value => value + 1)}>Try again</Button></div>}
      {!keys && !loadError && <div className="settings-row" role="status">Loading API keys…</div>}
      {keys?.length === 0 && <div className="settings-row"><p>No API keys issued yet.</p></div>}
      {keys?.map(key => {
        const status = key.revokedUtc ? "Revoked" : new Date(key.expiresUtc).getTime() <= now ? "Expired" : "Active";
        return <div key={key.id} className="settings-row settings-connection">
          <div className="settings-meta"><h4>{key.name} · {status}</h4><p>{key.access === "read" ? "Read-only" : "Read and write"}</p>
            <p><code>{key.prefix}…</code></p><p>Issued {time(key.createdUtc)} · Expires {time(key.expiresUtc)}</p>
            <p>Last used: {key.lastUsedUtc ? time(key.lastUsedUtc) : "Never"}</p>
          </div>
          {status === "Active" && <Button variant="outline" disabled={busy} onClick={() => { setError(""); setRevoke(key); }}>Revoke {key.name}</Button>}
        </div>;
      })}
    </Card>
    <Dialog open={!!issued} onOpenChange={open => { if (!open) { setIssued(null); setMessage(""); } }}>
      <DialogContent>
        <DialogHeader><DialogTitle>Save your API key</DialogTitle><DialogDescription>Copy this key now. You won’t be able to see it again after closing this window. Store it securely; anyone with it can use its assigned access until it expires or you revoke it.</DialogDescription></DialogHeader>
        <label htmlFor="issued-api-key">API key secret</label><Input ref={secretInput} id="issued-api-key" readOnly value={issued?.secret ?? ""} autoComplete="off" spellCheck={false} />
        <p className="muted">Send it in the Authorization header as Bearer followed by a space and the key. Write requests also require X-Homelab-Request: 1.</p>
        {message && <p role="status">{message}</p>}
        <div className="settings-actions"><Button onClick={() => void copy()}>Copy API key</Button><Button variant="outline" onClick={() => { setIssued(null); setMessage(""); }}>I’ve saved it</Button></div>
      </DialogContent>
    </Dialog>
    <Dialog open={!!revoke} onOpenChange={open => { if (!open && !busy) setRevoke(null); }}>
      <DialogContent><DialogHeader><DialogTitle>Revoke {revoke?.name}?</DialogTitle><DialogDescription>Clients using this key will lose access. Issue a replacement key before revoking if the integration needs to keep working.</DialogDescription></DialogHeader>
        {error && <p role="alert" className="error">{error}</p>}
        <div className="settings-actions"><Button variant="outline" disabled={busy} onClick={() => setRevoke(null)}>Cancel</Button><Button disabled={busy} onClick={() => void revokeKey()}>Revoke key</Button></div>
      </DialogContent>
    </Dialog>
  </>;
}
