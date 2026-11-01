import { useDisplayFormat } from "./displayPreferences";
import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
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
const attemptLabel = (result: string) => ({
  accepted: "Receiver accepted", rejected: "Receiver rejected", unavailable: "Receiver unavailable",
  "outcome-unknown": "Receiver outcome unknown",
}[result] ?? result);
export function NotificationSettings() {
  const { time } = useDisplayFormat();
  const [channel, setChannel] = useState<Channel | null>(null);
  const [deliveries, setDeliveries] = useState<Delivery[]>([]);
  const [attempts, setAttempts] = useState<Attempt[]>([]);
  const [url, setUrl] = useState("");
  const [kind, setKind] = useState<"webhook" | "telegram">("webhook");
  const [botToken, setBotToken] = useState("");
  const [chatId, setChatId] = useState("");
  const [mask, setMask] = useState(true);
  const [maintenance, setMaintenance] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  async function load() {
    const response = await fetch("/api/v1/notifications");
    if (!response.ok) throw new Error("Could not load notifications");
    const data = await response.json();
    setChannel(data.channel);
    setDeliveries(data.deliveries);
    setAttempts(data.attempts ?? []);
    if (data.channel) { setMask(data.channel.maskIdentifiers); setKind(data.channel.kind ?? "webhook"); }
  }
  useEffect(() => {
    void load().catch((e) => setMessage(String(e)));
  }, []);
  async function act(path: string, body: unknown, method = "POST") {
    setBusy(true);
    setMessage("");
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
        const data = await response.json();
        throw new Error(data.error || "Request failed");
      }
      setUrl(""); setBotToken(""); setChatId("");
      await load();
      setMessage(
        path === "/test"
          ? "Notifier accepted the test. You may now enable delivery."
          : "Notification settings saved.",
      );
    } catch (e) {
      setMessage(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  const body = (enabled: boolean) => ({
    kind,
    url: kind === "webhook" ? url || null : null,
    botToken: kind === "telegram" ? botToken || null : null,
    chatId: kind === "telegram" ? chatId || null : null,
    enabled,
    maskIdentifiers: mask,
    maintenanceUntilUtc: maintenance
      ? new Date(maintenance).toISOString()
      : null,
  });
  return (
    <section style={{ marginTop: 32 }}>
      <div className="section-title">
        <h2>Notifications</h2>
        <Badge variant="outline">
          {channel?.enabled ? "Enabled" : "Disabled"}
        </Badge>
      </div>
      <Card className="list-card">
        <form
          style={{ padding: "20px 0" }}
          onSubmit={(e) => {
            e.preventDefault();
            void act("", body(false), "PUT");
          }}
        >
          <div className="field">
            <Label htmlFor="notification-kind">Notification service</Label>
            <select id="notification-kind" value={kind} disabled={busy} onChange={e => { setKind(e.target.value as "webhook" | "telegram"); setUrl(""); setBotToken(""); setChatId(""); }}>
              <option value="webhook">HTTPS webhook</option><option value="telegram">Telegram</option>
            </select>
          </div>
          <p className="muted">Save the destination, send a test, then enable delivery. Changing the destination requires a new successful test.</p>
          {kind === "telegram" ? <>
            <p className="muted">Create a bot with <a href="https://t.me/BotFather" target="_blank" rel="noreferrer">BotFather</a>, then send your bot a message in Telegram. Enter its token and the chat ID below. For a group, add the bot first. Both values are encrypted on the server and never displayed again.</p>
            <div className="field"><Label htmlFor="telegram-token">Telegram bot token</Label><Input id="telegram-token" type="password" autoComplete="new-password" value={botToken} maxLength={221} onChange={e => setBotToken(e.target.value)} placeholder={channel?.kind === "telegram" ? "Leave both fields blank to keep saved destination" : "Token from BotFather"} /></div>
            <div className="field"><Label htmlFor="telegram-chat">Telegram chat ID</Label><Input id="telegram-chat" value={chatId} maxLength={40} onChange={e => setChatId(e.target.value)} placeholder="Numeric chat ID (groups may start with -)" /></div>
            <p className="muted">To change the token or chat, re-enter both fields. Alerts are sent through Telegram; masking below controls the private details included.</p>
          </> : <>
            <p className="muted">Enroll the destination hostname in the server setting Homelab__WebhookHosts__0.</p>
            <div className="field" style={{ marginTop: 16 }}>
              <Label htmlFor="webhook-url">HTTPS webhook URL {channel?.kind !== "telegram" && channel ? `(configured host: ${channel.host})` : ""}</Label>
              <Input id="webhook-url" type="password" value={url} placeholder={channel ? "Leave blank to keep existing destination" : "https://notifier.example/..."} onChange={e => setUrl(e.target.value)} />
            </div>
          </>}
          <label className="backup-checkbox">
            <input
              type="checkbox"
              checked={mask}
              onChange={(e) => setMask(e.target.checked)}
            />{" "}
            Mask hostnames, serials, and private evidence in outbound messages
          </label>
          <div className="field" style={{ marginTop: 16 }}>
            <Label htmlFor="notification-maintenance">
              Suppress noncritical deliveries until (browser timezone)
            </Label>
            <Input
              id="notification-maintenance"
              type="datetime-local"
              value={maintenance}
              onChange={(e) => setMaintenance(e.target.value)}
            />
          </div>
          <p className="muted">
            Critical findings bypass maintenance suppression. Accepted means the
            channel accepted the message, not that you read it. Failed
            deliveries stop after five attempts.
          </p>
          <div
            style={{
              display: "flex",
              flexWrap: "wrap",
              gap: 12,
              marginTop: 16,
            }}
          >
            <Button disabled={busy}>Save destination/settings</Button>
            <Button
              type="button"
              variant="outline"
              disabled={busy || !channel || kind !== (channel.kind ?? "webhook") || !!url || !!botToken || !!chatId}
              onClick={() => void act("/test", {})}
            >
              Send test
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={busy || !channel?.testedUtc || kind !== (channel?.kind ?? "webhook") || !!url || !!botToken || !!chatId}
              onClick={() => void act("", body(!channel?.enabled), "PUT")}
            >
              {channel?.enabled ? "Disable" : "Enable"} delivery
            </Button>
          </div>
        </form>
      </Card>
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
