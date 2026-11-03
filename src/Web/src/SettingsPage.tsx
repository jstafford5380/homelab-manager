import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link, Navigate, useBeforeUnload, useBlocker, useLocation } from "react-router";
import { Bell, Database, Gauge, Monitor, Plug, SlidersHorizontal, TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { AppearanceSettings } from "./AppearanceSettings";
import { ConnectionSettings, connectionStatus, type Connection } from "./ConnectionSettings";
import { HardwareSettings } from "./HardwareSettings";
import { NotificationSettings } from "./NotificationSettings";
import { OperationsSettings, type SettingsEnvelope } from "./OperationsSettings";
import type { DisplayPreferences } from "./displayPreferences";
import "./Settings.css";

const sections = {
  connections: { title: "Connections", icon: Plug, hint: "The systems Homelab Control reads from. Connections are read-only and never change NAS configuration." },
  monitoring: { title: "Monitoring", icon: Gauge, hint: "How often Homelab Control looks at drives, and which optional reads it makes. Every read is read-only." },
  "review-levels": { title: "Review levels", icon: SlidersHorizontal, hint: "Your own levels for when something is worth a look. They aren’t failure predictions or manufacturer safety limits." },
  notifications: { title: "Notifications", icon: Bell, hint: "Where findings are sent. Delivery turns on only after a successful test." },
  data: { title: "Data & backups", icon: Database, hint: "Keep monitoring history and back up Homelab Control’s own data." },
  display: { title: "Display", icon: Monitor, hint: "Theme applies right away in this browser. Timezone and units apply everywhere and save with the button below." },
};
type Section = keyof typeof sections;

async function settingsRequest<T>(path: string, signal?: AbortSignal): Promise<T> {
  const timeout = AbortSignal.timeout(15_000);
  const response = await fetch("/api/v1/" + path, {
    signal: signal ? AbortSignal.any([signal, timeout]) : timeout,
  });
  if (!response.ok) throw new Error(`Could not load ${path} (HTTP ${response.status}).`);
  return response.json();
}

function useSettingsLeaveGuard(dirty: boolean) {
  const blocker = useBlocker(({ currentLocation, nextLocation }) =>
    dirty && currentLocation.pathname !== nextLocation.pathname);
  useBeforeUnload(event => {
    if (!dirty) return;
    event.preventDefault();
    event.returnValue = "";
  });
  return blocker;
}

export function SettingsPage({ enrollment, connectionVersion, onDisplaySaved }: {
  enrollment: ReactNode;
  connectionVersion: string;
  onDisplaySaved: (settings: DisplayPreferences) => void;
}) {
  const location = useLocation();
  const [, , slug, id] = location.pathname.split("/");
  const section = Object.hasOwn(sections, slug) ? slug as Section : null;
  const [saved, setSaved] = useState<SettingsEnvelope | null>(null);
  const [connections, setConnections] = useState<Connection[] | null>(null);
  const [connectionError, setConnectionError] = useState("");
  const [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  const [dirty, setDirty] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  const navigation = useRef<HTMLElement>(null);
  const leave = useSettingsLeaveGuard(dirty);
  useEffect(() => {
    const controller = new AbortController();
    void settingsRequest<SettingsEnvelope>("settings", controller.signal).then(setSaved).catch(error => {
      if (!controller.signal.aborted) setError(error.message);
    });
    return () => controller.abort();
  }, [retry]);
  useEffect(() => {
    const controller = new AbortController();
    void settingsRequest<Connection[]>("connections", controller.signal).then(rows => {
      setConnections(rows);
      setConnectionError("");
    }).catch(error => {
      if (!controller.signal.aborted) setConnectionError(error.message);
    });
    return () => controller.abort();
  }, [connectionVersion, retry]);
  const ready = !!saved || !!error;
  useEffect(() => {
    heading.current?.focus({ preventScroll: true });
    window.scrollTo({ top: 0, behavior: "instant" });
    const nav = navigation.current;
    const active = nav?.querySelector("[aria-current]");
    if (nav && active && nav.scrollWidth > nav.clientWidth) {
      const left = active.getBoundingClientRect().left - nav.getBoundingClientRect().left + nav.scrollLeft;
      nav.scrollLeft = left - (nav.clientWidth - active.clientWidth) / 2;
    }
  }, [section, id, ready]);
  if (!section) return <Navigate replace to={"/settings/connections" + location.search + location.hash} />;
  const loading = !saved && !error;
  const attention = (connections ?? []).filter(connection => connectionStatus(connection).attention).length;
  const current = connections?.find(connection => connection.id === id);
  return <div className="settings-layout">
    <Dialog open={leave.state === "blocked"} onOpenChange={open => {
      if (!open && leave.state === "blocked") leave.reset();
    }}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Discard unsaved changes?</DialogTitle>
          <DialogDescription>Leave this section and discard your edits, or stay to keep working.</DialogDescription>
        </DialogHeader>
        <div className="settings-actions">
          <Button type="button" variant="outline" onClick={() => leave.reset?.()}>Stay</Button>
          <Button type="button" onClick={() => leave.proceed?.()}>Discard and leave</Button>
        </div>
      </DialogContent>
    </Dialog>
    <nav ref={navigation} aria-label="Settings sections" className="settings-nav">
      {(Object.entries(sections) as [Section, typeof sections[Section]][]).map(([key, { title, icon: Icon }]) => {
        const content = <><Icon size={17} aria-hidden="true" />{title}
          {key === "connections" && attention > 0 && <span className="settings-attention" aria-label={`${attention} connections need attention`}>{attention}</span>}
          {dirty && section === key && <span className="settings-dirty" role="img" aria-label="Unsaved changes" />}</>;
        return loading
          ? <span key={key} aria-disabled="true">{content}</span>
          : <Link key={key} to={`/settings/${key}`} aria-current={section === key ? "page" : undefined}>{content}</Link>;
      })}
    </nav>
    <div className="settings-content">
      {loading ? <Card className="settings-skeleton" aria-busy="true"><p role="status">Loading settings…</p><div /><div /></Card> : <>
        {error && <Card className="settings-unavailable" role="alert">
          <h2><TriangleAlert size={18} aria-hidden="true" /> Settings couldn’t be loaded</h2>
          <p>{error}</p><p>Monitoring keeps running with the last saved settings. Nothing here has changed.</p>
          <Button variant="outline" onClick={() => { setError(""); setRetry(value => value + 1); }}>Try again</Button>
        </Card>}
        {(saved || section === "display") && <>
          {section === "connections" && id && <Link to="/settings/connections">← All connections</Link>}
          <div className="settings-section-heading">
            <div><h2 ref={heading} tabIndex={-1}>{id && section === "connections" ? current?.name ?? "Connection details" : sections[section].title}</h2>
              <p className="muted">{id && section === "connections" ? "Changes here affect how Homelab Control reads this system. They don’t modify the NAS." : sections[section].hint}</p></div>
            {section === "connections" && !id && enrollment}
          </div>
          {section === "connections" && <>
            {connections || connectionError ? <ConnectionSettings key={id ?? "list"} id={id} connections={connections ?? []} error={connectionError}
              onConnections={setConnections} onDirty={setDirty} /> : <p role="status">Loading connections…</p>}
            {!id && <HardwareSettings />}
          </>}
          {section === "notifications" && <NotificationSettings onDirty={setDirty} />}
          {section === "display" && !saved && <Card className="settings-group"><AppearanceSettings /></Card>}
          {saved && section !== "connections" && section !== "notifications" &&
            <OperationsSettings key={section} section={section} saved={saved} onDirty={setDirty}
              onSaved={next => { setSaved(next); if (section === "display") onDisplaySaved(next.settings); }} />}
        </>}
      </>}
    </div>
  </div>;
}
