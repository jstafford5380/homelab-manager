import { useDisplayFormat } from "./displayPreferences";
import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { SettingsSaveBar } from "./SettingsSaveBar";
import { Badge } from "@/components/ui/badge";
type Channel = {
  kind?: "webhook" | "telegram";
  host: string;
  enabled: boolean;
  maskIdentifiers: boolean;
  testedUtc: string | null;
  maintenanceUntilUtc: string | null;
};
type Delivery = {
  id: string;
  kind: string;
  status: string;
  attempts: number;
  createdUtc: string;
};
type Attempt = { id: number; deliveryId: string; atUtc: string; result: string; httpStatus: number | null };
function localTime(value: string | null | undefined) {
  if (!value) return "";
  const date = new Date(value);
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}
const attemptLabel = (result: string) => ({
  accepted: "Receiver accepted", rejected: "Receiver rejected", unavailable: "Receiver unavailable",
  "outcome-unknown": "Receiver outcome unknown",
}[result] ?? result);
export function NotificationSettings({ onDirty }: { onDirty: (dirty: boolean) => void }) {
  const { time } = useDisplayFormat();
  const [channel, setChannel] = useState<Channel | null>(null);
  const [deliveries, setDeliveries] = useState<Delivery[]>([]);
  const [attempts, setAttempts] = useState<Attempt[]>([]);
  const [showEnableStep, setShowEnableStep] = useState(false);
  const [changing, setChanging] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [url, setUrl] = useState("");
  const [kind, setKind] = useState<"webhook" | "telegram">("webhook");
  const [botToken, setBotToken] = useState("");
  const [chatId, setChatId] = useState("");
  const [mask, setMask] = useState(true);
  const [maintenance, setMaintenance] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [preferenceError, setPreferenceError] = useState("");
  const [quietError, setQuietError] = useState("");
  const changes = channel ? Number(mask !== channel.maskIdentifiers) + Number(maintenance !== localTime(channel.maintenanceUntilUtc)) : 0;
  const destinationDirty = !!(url || botToken || chatId) || kind !== (channel?.kind ?? "webhook");
  useEffect(() => { onDirty(changes > 0 || destinationDirty); }, [changes, destinationDirty, onDirty]);
  useEffect(() => () => onDirty(false), [onDirty]);
  useEffect(() => {
    if (!busy && quietError) document.getElementById("notification-maintenance")?.focus();
  }, [busy, quietError]);

  function discardPreferences() {
    setMask(channel?.maskIdentifiers ?? true);
    setMaintenance(localTime(channel?.maintenanceUntilUtc));
    setPreferenceError("");
    setQuietError("");
  }
  async function load(keepPreferences = false) {
    const response = await fetch("/api/v1/notifications");
    if (!response.ok) throw new Error("Could not load notifications");
    const data = await response.json();
    setChannel(data.channel);
    setDeliveries(data.deliveries);
    setAttempts(data.attempts ?? []);
    if (data.channel) {
      if (!keepPreferences) {
        setMask(data.channel.maskIdentifiers);
        setMaintenance(localTime(data.channel.maintenanceUntilUtc));
      }
      setKind(data.channel.kind ?? "webhook");
    }
    setLoaded(true);
  }
  useEffect(() => {
    void load().catch((e) => setMessage(String(e)));
  }, []);
  async function act(path: string, body: unknown, method = "POST", preferences = false) {
    setBusy(true);
    setMessage("");
    if (preferences) { setPreferenceError(""); setQuietError(""); }
    try {
      const response = await fetch("/api/v1/notifications" + path, {
        method,
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const data = await response.json().catch(() => ({}));
        if (preferences && data.errors?.maintenanceUntilUtc) {
          setQuietError(data.errors.maintenanceUntilUtc.join(" "));
          throw new Error("Not saved. Fix the highlighted field and try again.");
        }
        throw new Error(data.error || "Request failed");
      }
      const destination = changing && !preferences && path === "";
      if (destination || !channel) {
        setUrl(""); setBotToken(""); setChatId("");
        setChanging(false);
        setShowEnableStep(true);
      }
      await load(!preferences && changes > 0);
      if (path === "/test" && !channel?.testedUtc) setShowEnableStep(true);
      if (path === "" && !destination && !preferences && (body as { enabled?: boolean }).enabled) setShowEnableStep(false);
      setMessage(
        path === "/test"
          ? `Accepted means received by ${channel?.kind === "telegram" ? "Telegram" : "the webhook receiver"}, not read.`
          : "Notification settings saved.",
      );
    } catch (e) {
      const error = e instanceof Error ? e.message : String(e);
      if (preferences) setPreferenceError(error);
      else setMessage(error);
    } finally {
      setBusy(false);
    }
  }
  if (!loaded) return message ? <Card className="settings-unavailable">
    <p role="alert">{message}</p><Button variant="outline" onClick={() => void load().catch(error => setMessage(error.message))}>Try again</Button>
  </Card> : <p role="status">Loading notifications…</p>;
  const editingDestination = changing || !channel;
  const summary = channel?.kind === "telegram" ? "Telegram" : channel?.host;
  function savedBody(enabled: boolean) {
    return { kind: channel?.kind, enabled, maskIdentifiers: channel?.maskIdentifiers, maintenanceUntilUtc: channel?.maintenanceUntilUtc };
  }
  const toggleDelivery = <label className="backup-checkbox"><input type="checkbox" role="switch"
    checked={!!channel?.enabled} disabled={busy || !channel?.testedUtc || editingDestination}
    onChange={event => void act("", savedBody(event.target.checked), "PUT")} /> Delivery enabled</label>;
  return (
    <section className="settings-form">
      <Card className="settings-group">
        <div className="settings-group-heading"><h3>Delivery setup</h3>
          <Badge variant="outline">{channel?.enabled ? "Delivering" : channel ? "Delivery off" : "Not set up"}</Badge>
        </div>
        {channel?.testedUtc && !showEnableStep && !editingDestination ? <div className="settings-row">
          <div><strong>{summary}</strong><p className="muted">Test accepted {time(channel.testedUtc)}.</p></div>
          <div className="settings-actions">
            <Button variant="outline" onClick={() => setChanging(true)} disabled={busy}>Change</Button>
            <Button variant="outline" onClick={() => void act("/test", {})} disabled={busy}>Send another</Button>
            {toggleDelivery}
          </div>
        </div> : <ol className="settings-steps">
          <li><h4>1. Choose a destination</h4>
            {editingDestination ? <form onSubmit={event => {
              event.preventDefault();
              void act("", {
                ...savedBody(false), kind, maskIdentifiers: channel?.maskIdentifiers ?? true,
                url: kind === "webhook" ? url : null,
                botToken: kind === "telegram" ? botToken : null,
                chatId: kind === "telegram" ? chatId : null,
              }, "PUT");
            }}>
          <div className="field">
            <Label htmlFor="notification-kind">Notification service</Label>
            <select id="notification-kind" value={kind} disabled={busy} onChange={e => { setKind(e.target.value as "webhook" | "telegram"); setUrl(""); setBotToken(""); setChatId(""); }}>
              <option value="webhook">HTTPS webhook</option><option value="telegram">Telegram</option>
            </select>
          </div>
          <p className="muted">Save the destination, send a test, then enable delivery. Changing the destination requires a new successful test.</p>
          {kind === "telegram" ? <>
            <p className="muted">Create a bot with <a href="https://t.me/BotFather" target="_blank" rel="noreferrer">BotFather</a>, then send your bot a message in Telegram. Enter its token and the chat ID below. For a group, add the bot first. Both values are encrypted on the server and never displayed again.</p>
            <div className="field"><Label htmlFor="telegram-token">Telegram bot token</Label><Input id="telegram-token" type="password" autoComplete="new-password" value={botToken} maxLength={221} onChange={e => setBotToken(e.target.value)} required placeholder="Token from BotFather" /></div>
            <div className="field"><Label htmlFor="telegram-chat">Telegram chat ID</Label><Input id="telegram-chat" value={chatId} maxLength={40} onChange={e => setChatId(e.target.value)} required placeholder="Numeric chat ID (groups may start with -)" /></div>
            <p className="muted">To change the token or chat, re-enter both fields. Alerts are sent through Telegram; the privacy setting controls the private details included.</p>
          </> : <>
            <p className="muted">Enroll the destination hostname in the server setting Homelab__WebhookHosts__0.</p>
            <div className="field" style={{ marginTop: 16 }}>
              <Label htmlFor="webhook-url">HTTPS webhook URL {channel?.kind !== "telegram" && channel ? `(configured host: ${channel.host})` : ""}</Label>
              <Input id="webhook-url" type="password" value={url} required placeholder="https://notifier.example/..." onChange={e => setUrl(e.target.value)} />
            </div>
          </>}
              <div className="settings-actions">
                <Button disabled={busy}>Save destination</Button>
                {channel && <Button type="button" variant="outline" disabled={busy} onClick={() => {
                  setChanging(false); setUrl(""); setBotToken(""); setChatId(""); setKind(channel.kind ?? "webhook");
                }}>Cancel</Button>}
              </div>
            </form> : <><p>{summary}</p><Button variant="outline" disabled={busy} onClick={() => setChanging(true)}>Change</Button></>}
          </li>
          <li><h4>2. Send a test</h4>
            {editingDestination && <p className="muted">Available after the destination is saved.</p>}
            <Button variant="outline" disabled={busy || editingDestination} onClick={() => void act("/test", {})}>Send a test</Button>
          </li>
          <li><h4>3. Turn on delivery</h4>
            {(!channel?.testedUtc || editingDestination) && <p className="muted">Available after the receiver accepts a test.</p>}
            <Button variant="outline" disabled={busy || editingDestination || !channel?.testedUtc}
              onClick={() => void act("", savedBody(true), "PUT")}>Turn on delivery</Button>
          </li>
        </ol>}
      </Card>
      {channel && <Card className="settings-group">
        <form onSubmit={event => { event.preventDefault(); void act("", {
          ...savedBody(channel.enabled), maskIdentifiers: mask, maintenanceUntilUtc: maintenance ? new Date(maintenance).toISOString() : null,
        }, "PUT", true); }}>
          <h3>Quiet period and privacy</h3>
          <fieldset disabled={editingDestination || busy}>
          <label className="backup-checkbox">
            <input
              type="checkbox"
              checked={mask}
              onChange={(e) => setMask(e.target.checked)}
            />{" "}
            Mask hostnames, serials, and private evidence in outbound messages
          </label>
          {mask !== channel.maskIdentifiers && <p className="muted">Was {channel.maskIdentifiers ? "On" : "Off"}</p>}
          <div className="field" style={{ marginTop: 16 }}>
            <Label htmlFor="notification-maintenance">
              Suppress noncritical deliveries until (browser timezone)
            </Label>
            <Input
              id="notification-maintenance"
              type="datetime-local"
              value={maintenance}
              aria-invalid={!!quietError}
              aria-describedby={quietError ? "notification-maintenance-error" : undefined}
              onChange={event => { setMaintenance(event.target.value); setQuietError(""); }}
            />
            {maintenance !== localTime(channel.maintenanceUntilUtc) && <p className="muted">Was {channel.maintenanceUntilUtc ? time(channel.maintenanceUntilUtc) : "Off"}</p>}
            {quietError && <p className="error" id="notification-maintenance-error">{quietError}</p>}
          </div>
          <p className="muted">
            Critical findings bypass maintenance suppression. Accepted means the
            channel accepted the message, not that you read it. Failed
            deliveries stop after five attempts.
          </p>
          </fieldset>
          {changes > 0 && <SettingsSaveBar section="Notifications" count={changes} error={preferenceError} busy={busy || editingDestination}
            onDiscard={discardPreferences} />}
        </form>
      </Card>}
      {message && (
        <p role="status" style={{ margin: "16px 0" }}>
          {message}
        </p>
      )}
      <h3 style={{ margin: "24px 0 12px" }}>Delivery history</h3>
      <Card className="list-card">
        {deliveries.length ? (
          deliveries.map((d) => (
            <div className="notification-delivery" key={d.id}>
              <div>
                <h3>{d.kind}</h3>
                <small>
                  {time(d.createdUtc)} · {d.attempts} attempts
                </small>
                {attempts.some(a => a.deliveryId === d.id) && (
                  <details style={{ marginTop: 8 }}>
                    <summary>Recent attempt details</summary>
                    <ul>
                      {attempts.filter(a => a.deliveryId === d.id).map(a => (
                        <li key={a.id}>
                          {time(a.atUtc)} · {attemptLabel(a.result)}{a.httpStatus !== null ? ` (HTTP ${a.httpStatus})` : ""}
                        </li>
                      ))}
                    </ul>
                    <p className="muted">An interrupted send may have reached the receiver. Retrying can deliver the same event again; acceptance does not prove it was read.</p>
                  </details>
                )}
              </div>
              <Badge variant="outline">{d.status}</Badge>
              {d.status === "failed" && (
                <Button
                  variant="outline"
                  disabled={busy || !channel?.enabled}
                  onClick={() => void act(`/deliveries/${d.id}/retry`, {})}
                >
                  Retry delivery
                </Button>
              )}
            </div>
          ))
        ) : (
          <p className="muted" style={{ padding: 16 }}>
            No queued deliveries.
          </p>
        )}
      </Card>
    </section>
  );
}
