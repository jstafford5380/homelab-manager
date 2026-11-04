import {useEffect,useState} from "react";
import {useSearchParams} from "react-router";
import {Button} from "@/components/ui/button";
import {Card} from "@/components/ui/card";
import {Label} from "@/components/ui/label";
import {Input} from "@/components/ui/input";
import {hardwareRequest} from "../hardware/hardwareModel";
type Setup = {
  id: string;
  name: string;
  host: string;
  connect: boolean;
  credentialConfigured: boolean;
};
export function HardwareSettings() {
  const [enabled, setEnabled] = useState(false),
    [servers, setServers] = useState<Setup[]>([]),
    [editing, setEditing] = useState<Setup | null>(null),
    [message, setMessage] = useState(""),
    [busy, setBusy] = useState(false);
  const [showSetup, setShowSetup] = useState(false);
  const [params] = useSearchParams();
  useEffect(() => {
    const controller = new AbortController();
    void hardwareRequest("/servers", undefined, "GET", controller.signal)
      .then(async (flags) => {
        if (!flags.synology) return;
        setEnabled(true);
        const rows = await hardwareRequest(
          "/synology/settings",
          undefined,
          "GET",
          controller.signal,
        );
        setServers(rows);
        setEditing(
          rows.find((s: Setup) => s.id === params.get("server")) ?? null,
        );
      })
      .catch(() => {
        /* The regular connection settings remain available if optional setup cannot load. */
      });
    return () => controller.abort();
  }, [params]);
  if (!enabled) return null;
  return (
    <section id="synology" style={{ marginTop: 32 }}>
      <div className="settings-section-heading"><h3>Synology hardware</h3>
      <Button variant="outline" onClick={() => { setEditing(null); setShowSetup(value => !value); }}>Add Synology hardware</Button></div>
      <p className="muted">
        Enroll a Synology server for entered parts and readings, or connect
        through read-only SNMP v2c on UDP 161. SNMP v2c sends the community
        unencrypted: use it only on your trusted management network and restrict
        access in DSM. No DSM settings or storage are changed.
      </p>
      {servers.map((s) => (
        <p key={s.id}>
          {s.name} · {s.host} ·{" "}
          {s.connect ? "Automatic collection configured" : "Entered by hand"}{" "}
          <Button variant="outline" onClick={() => setEditing(s)}>
            Edit {s.name}
          </Button>
        </p>
      ))}
      {(editing || showSetup) && <Card style={{ padding: 20, marginTop: 16 }}>
        <h3>{editing ? `Edit ${editing.name}` : "Enroll a Synology server"}</h3>
        <form
          key={editing?.id ?? "new"}
          onSubmit={async (e) => {
            e.preventDefault();
            const form = new FormData(e.currentTarget);
            setBusy(true);
            setMessage("");
            try {
              await hardwareRequest(
                "/synology" + (editing ? "/" + editing.id : ""),
                {
                  name: form.get("name"),
                  host: form.get("host"),
                  community: form.get("community") || null,
                  connect: form.get("connect") === "on",
                },
                editing ? "PUT" : "POST",
              );
              setServers(await hardwareRequest("/synology/settings"));
              setEditing(null);
              setShowSetup(false);
              setMessage("Saved");
            } catch (err) {
              setMessage(err instanceof Error ? err.message : String(err));
            } finally {
              setBusy(false);
            }
          }}
        >
          <div className="settings-grid">
            <div className="field">
              <Label htmlFor="synology-name">Synology display name</Label>
              <Input
                id="synology-name"
                name="name"
                required
                maxLength={200}
                defaultValue={editing?.name}
              />
            </div>
            <div className="field">
              <Label htmlFor="synology-host">Synology IP address</Label>
              <Input
                id="synology-host"
                name="host"
                required
                maxLength={100}
                defaultValue={editing?.host}
              />
            </div>
          </div>
          <div className="field">
            <Label htmlFor="synology-community">
              Read-only SNMP community{" "}
              {editing?.credentialConfigured
                ? "(leave blank to keep saved)"
                : "(optional for entered readings)"}
            </Label>
            <Input
              id="synology-community"
              name="community"
              type="password"
              maxLength={200}
              autoComplete="off"
            />
          </div>
          <label>
            <input
              type="checkbox"
              name="connect"
              defaultChecked={editing?.connect}
            />{" "}
            Collect automatically
          </label>
          <div style={{ marginTop: 16 }}>
            <Button disabled={busy}>Save Synology server</Button>
            {editing && (
              <Button
                type="button"
                variant="outline"
                onClick={() => setEditing(null)}
              >
                Cancel
              </Button>
            )}
          </div>
        </form>
      </Card>}
      <p role="status">{message}</p>
    </section>
  );
}
