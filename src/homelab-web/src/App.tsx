import { ActivityHistory } from "./ActivityHistory";
import { ScanOverview, validScanReports, type ScanReport } from "./ScanOverview";
import {
  DisplayPreferencesContext,
  type DisplayPreferences,
} from "./displayPreferences";
import { PlanEditor, type Plan } from "./PlanEditor";
import { Link, useLocation, useNavigate, useSearchParams } from "react-router";
import { FindingDetail } from "./FindingDetail";
import { useEffect, useRef, useState } from "react";
import {
  Activity,
  ArrowUpRight,
  Bell,
  Boxes,
  Check,
  ChevronRight,
  Database,
  HardDrive,
  LayoutDashboard,
  LogOut,
  Plus,
  RefreshCw,
  Settings,
  ShieldCheck,
  ShoppingBag,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import "./App.css";
import { ConnectionSettings } from "./ConnectionSettings";
import { InventoryView } from "./InventoryView";
import { RecoveryView } from "./RecoveryView";
import { NotificationSettings } from "./NotificationSettings";
import { AppearanceSettings } from "./AppearanceSettings";
import { OperationsSettings } from "./OperationsSettings";
import { PlanningReview } from "./PlanningReview";
import { StorageView } from "./StorageView";

type Connection = {
  id: string;
  name: string;
  status: string;
  lastSuccessUtc: string | null;
  pollSeconds: number;
};
type Disk = {
  id: string;
  connectionId: string;
  lastSeenUtc: string;
  serial: string;
  model: string;
  alias: string;
  wwn: string | null;
  mappingConfidence: string;
  bay: string | null;
  bayVerified: boolean;
  bayEvidence: string | null;
  present: boolean;
  purchasedUtc: string | null;
  warrantyEndUtc: string | null;
  notes: string;
  lifecycle: string;
  purchaseDateConfidence: string;
};
type Finding = {
  id: string;
  title: string;
  evidence: string;
  nextStep: string;
  severity: string;
  state: string;
  snoozedUntilUtc: string | null;
  reviewUtc: string | null;
};
type Recovery = {
  id: string;
  asset: string;
  destination: string;
  mechanism: string;
  rpoHours: number;
  recoveryPointUtc: string | null;
  restoreResult: string;
};
type Snapshot = {
  scanReports?: ScanReport[];
  recoveryAssessments: {
    policyId: string;
    assessment: { protection: string };
  }[];
  capabilities: { connectionId: string; name: string; status: string }[];
  settings: DisplayPreferences;
  connections: Connection[];
  disks: Disk[];
  findings: Finding[];
  plans: Plan[];
  recovery: Recovery[];
  activity: { id: number; action: string; detail: string; atUtc: string }[];
};
const empty: Snapshot = {
  recoveryAssessments: [],
  capabilities: [],
  settings: { timezone: "America/Phoenix", capacityUnits: "binary" },
  connections: [],
  disks: [],
  findings: [],
  plans: [],
  recovery: [],
  activity: [],
};
async function request(
  path: string,
  body?: unknown,
  method = "POST",
  signal?: AbortSignal,
) {
  const response = await fetch(
    "/api/v1" + path,
    body === undefined
      ? { signal }
      : {
          method,
          signal,
          headers: {
            "Content-Type": "application/json",
            "X-Homelab-Request": "1",
          },
          body: JSON.stringify(body),
        },
  );
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    throw new Error(data.error || `Request failed (${response.status})`);
  }
  return response.status === 204 ? null : response.json().catch(() => null);
}
const nav = [
  { title: "Overview", path: "/", icon: LayoutDashboard },
  { title: "Storage", path: "/storage", icon: HardDrive },
  { title: "Recovery", path: "/recovery", icon: ShieldCheck },
  { title: "Planning", path: "/planning", icon: ShoppingBag },
  { title: "Inventory", path: "/inventory", icon: Boxes },
  { title: "Activity", path: "/activity", icon: Activity },
  { title: "Settings", path: "/settings", icon: Settings },
];
function Field({
  name,
  label,
  type = "text",
  required = true,
  placeholder,
}: {
  name: string;
  label: string;
  type?: string;
  required?: boolean;
  placeholder?: string;
}) {
  return (
    <div className="field">
      <Label htmlFor={name}>{label}</Label>
      <Input
        id={name}
        name={name}
        type={type}
        required={required}
        placeholder={placeholder}
      />
    </div>
  );
}
function date(value: string | null, timezone: string) {
  return value
    ? new Date(value).toLocaleString(undefined, { timeZone: timezone })
    : "Not yet observed";
}
export default function App() {
  const [session, setSession] = useState<{
    authenticated: boolean;
    setupRequired: boolean;
  } | null>(null);
  const [snapshot, setSnapshot] = useState<Snapshot>(empty);
  const [refreshPhase, setRefreshPhase] = useState<
    "checking" | "current" | "unavailable"
  >("checking");
  const [refreshError, setRefreshError] = useState("");
  const [lastRefresh, setLastRefresh] = useState<number | null>(null);
  const refreshController = useRef<AbortController | null>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const page =
    nav.find((item) => item.path === location.pathname)?.title ??
    "Page not found";
  const findingId = page === "Overview" ? searchParams.get("finding") : null;
  const setPage = (title: string) => {
    void navigate(nav.find((item) => item.title === title)?.path ?? "/");
  };
  useEffect(() => {
    window.scrollTo({ top: 0, behavior: "instant" });
    headingRef.current?.focus({ preventScroll: true });
    document.title = `${findingId ? "Finding evidence" : page} · Homelab Control`;
  }, [page, findingId, session?.authenticated]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [modal, setModal] = useState(false);
  useEffect(() => {
    setModal(false);
  }, [location.pathname]);
  async function refresh() {
    refreshController.current?.abort();
    const controller = new AbortController();
    refreshController.current = controller;
    let timedOut = false;
    const timeout = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, 15000);
    setRefreshPhase("checking");
    try {
      const state = await request(
        "/session",
        undefined,
        "GET",
        controller.signal,
      );
      if (
        !state ||
        typeof state.authenticated !== "boolean" ||
        typeof state.setupRequired !== "boolean"
      )
        throw new Error("Session data could not be read.");
      if (refreshController.current !== controller) return;
      setSession(state);
      if (state.authenticated) {
        const next = await request(
          "/overview",
          undefined,
          "GET",
          controller.signal,
        );
        if (
          !next ||
          !next.settings ||
          typeof next.settings.timezone !== "string" ||
          ![
            "recoveryAssessments",
            "capabilities",
            "connections",
            "disks",
            "findings",
            "plans",
            "recovery",
            "activity",
          ].every(
            (key) =>
              Array.isArray(next[key]) &&
              next[key].every(
                (row: unknown) => row !== null && typeof row === "object",
              ),
          ) ||
          (next.scanReports !== undefined && !validScanReports(next.scanReports)) ||
          !next.recoveryAssessments.every(
            (row: { assessment?: { protection?: unknown } }) =>
              typeof row.assessment?.protection === "string",
          )
        )
          throw new Error("Overview data could not be read.");
        new Intl.DateTimeFormat(undefined, {
          timeZone: next.settings.timezone,
        });
        if (refreshController.current !== controller) return;
        setSnapshot(next);
        setLastRefresh(Date.now());
      } else {
        setSnapshot(empty);
        setLastRefresh(null);
      }
      setRefreshPhase("current");
      setRefreshError("");
    } catch (e) {
      if (refreshController.current !== controller) return;
      setRefreshPhase("unavailable");
      setRefreshError(
        timedOut
          ? "The refresh timed out. The control service did not respond within 15 seconds."
          : e instanceof Error
            ? e.message
            : "The overview could not be refreshed.",
      );
    } finally {
      clearTimeout(timeout);
    }
  }
  useEffect(() => {
    void refresh();
    const timer = setInterval(() => void refresh(), 30000);
    return () => {
      clearInterval(timer);
      refreshController.current?.abort();
      refreshController.current = null;
    };
  }, []);
  async function action(callback: () => Promise<unknown>) {
    setBusy(true);
    setError("");
    try {
      await callback();
      await refresh();
      setModal(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  const recoveryGaps = snapshot.recoveryAssessments.filter(
    (r) => r.assessment.protection !== "scoped recovery demonstrated",
  ).length;
  const issues = snapshot.findings.filter((x) => x.state !== "resolved");
  const coverageGaps = snapshot.capabilities.filter(
    (x) => x.status !== "supported",
  );
  const unchecked = snapshot.connections.filter(
    (x) => !snapshot.capabilities.some((c) => c.connectionId === x.id),
  );
  if (!session)
    return (
      <div className="login">
        <Database />
        <h1>Homelab Control</h1>
        <p>Connecting to your control service…</p>
        {(error || refreshError) && <p role="alert">{error || refreshError}</p>}
        {refreshError && (
          <Button variant="outline" onClick={() => void refresh()}>
            Retry connection
          </Button>
        )}
      </div>
    );
  if (!session.authenticated)
    return (
      <div className="login">
        <div className="brand-icon">
          <Database size={24} />
        </div>
        <p className="eyebrow">YOUR INFRASTRUCTURE, UNDERSTOOD</p>
        <h1>Homelab Control</h1>
        <p>A clearer picture of your storage. A plan for what comes next.</p>
        <Card className="login-card">
          <h2>
            {session.setupRequired
              ? "Create your owner account"
              : "Welcome back"}
          </h2>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              const values = Object.fromEntries(new FormData(e.currentTarget));
              void action(async () => {
                if (session.setupRequired) await request("/setup", values);
                await request("/login", values);
              });
            }}
          >
            <Field name="username" label="Username" />
            <Field name="password" label="Password" type="password" />
            {session.setupRequired && (
              <>
                <Field
                  name="setupToken"
                  label="Server setup token"
                  type="password"
                />
                <p className="muted">
                  Use the token configured on your control service. Choose a
                  password of at least 9 characters.
                </p>
              </>
            )}
            <Button disabled={busy} className="w-full">
              {session.setupRequired ? "Create account" : "Sign in"}{" "}
              <ArrowUpRight size={16} />
            </Button>
            {error && (
              <p role="alert" className="error">
                {error}
              </p>
            )}
          </form>
        </Card>
      </div>
    );
  return (
    <DisplayPreferencesContext.Provider value={snapshot.settings}>
      <div className="shell">
        <a
          className="skip-link"
          href="#main-content"
          onClick={() => document.getElementById("main-content")?.focus()}
        >
          Skip to main content
        </a>
        <aside>
          <Link className="brand" to="/">
            <span className="brand-icon">
              <Database size={20} />
            </span>
            <span>
              homelab<span className="brand-light"> / control</span>
            </span>
          </Link>
          <div className="workspace">
            <span className="workspace-dot" />
            <div>
              <strong>My homelab</strong>
              <small>Personal workspace</small>
            </div>
            <ChevronRight size={15} />
          </div>
          <p className="nav-label">WORKSPACE</p>
          <nav aria-label="Main navigation">
            {nav.map((item) => (
              <Link
                key={item.title}
                className={page === item.title ? "active" : ""}
                to={item.path}
                aria-label={item.title}
                aria-current={page === item.title ? "page" : undefined}
              >
                <item.icon size={18} />
                {item.title}
                {item.title === "Overview" && issues.length > 0 && (
                  <span className="nav-count">{issues.length}</span>
                )}
              </Link>
            ))}
          </nav>
          <div className="aside-bottom">
            <ShieldCheck size={18} />
            <div>
              <strong>Read-only monitoring</strong>
              <small>Your NAS stays in your control.</small>
            </div>
          </div>
          <button
            className="logout"
            onClick={() => void action(() => request("/logout", {}))}
          >
            <LogOut size={16} /> Sign out
          </button>
        </aside>
        <div className="main">
          <header>
            <span>
              Workspace <ChevronRight size={14} /> <strong>{page}</strong>
            </span>
            <span
              className="header-right"
              role="status"
              aria-label="Overview refresh status"
              aria-live="off"
            >
              <span className={`status-dot status-${refreshPhase}`} />{" "}
              {refreshPhase === "current"
                ? "Overview refreshed"
                : refreshPhase === "checking"
                  ? "Refreshing overview"
                  : "Overview unavailable"}{" "}
              <Bell size={18} />
            </span>
          </header>
          <main id="main-content" tabIndex={-1}>
            {refreshError && (
              <section className="refresh-warning" role="alert">
                <div>
                  <strong>Overview refresh unavailable</strong>
                  <p>
                    {lastRefresh
                      ? `Showing last-known overview data loaded ${new Date(lastRefresh).toLocaleString(undefined, { timeZone: snapshot.settings.timezone })}.`
                      : "No overview data has been loaded in this session."}{" "}
                    This does not establish the current condition of your NAS.
                  </p>
                  <small>{refreshError}</small>
                </div>
                <Button variant="outline" onClick={() => void refresh()}>
                  Retry overview
                </Button>
              </section>
            )}
            <div className="page-heading">
              <div>
                <p className="eyebrow">HOMELAB CONTROL</p>
                <h1 ref={headingRef} tabIndex={-1}>
                  {findingId
                    ? "Finding evidence"
                    : page === "Overview"
                      ? "Your storage, at a glance."
                      : page}
                </h1>
                <p className="muted">
                  {findingId
                    ? "Recorded evidence, source details, and review history."
                    : page === "Overview"
                      ? "Know what needs attention today. Prepare for what comes next."
                      : page === "Planning"
                        ? "Turn observations into a practical replacement plan."
                        : "Evidence, history, and the details that matter."}
                </p>
              </div>
              <Button variant="outline" onClick={() => void refresh()}>
                <RefreshCw size={15} /> Refresh
              </Button>
            </div>
            {error && (
              <div className="error" role="alert">
                {error}
              </div>
            )}
            {page === "Page not found" && (
              <Card className="empty">
                <h2>This page does not exist</h2>
                <p>Use the navigation or return to your overview.</p>
                <Button asChild>
                  <Link to="/">Go to overview</Link>
                </Button>
              </Card>
            )}
            {findingId && (
              <FindingDetail
                id={findingId}
                timezone={snapshot.settings.timezone}
                onChanged={refresh}
              />
            )}
            {page === "Overview" && !findingId && lastRefresh === null && (
              <Card className="empty">
                <h2>
                  {refreshError
                    ? "Overview not available yet"
                    : "Loading your overview…"}
                </h2>
                <p>
                  Your saved inventory and findings will appear after a
                  successful refresh.
                </p>
              </Card>
            )}
            {page === "Overview" && !findingId && lastRefresh !== null && (
              <>
                <div className="stats">
                  <Card>
                    <span className="stat-label">
                      OBSERVED CONDITION <Activity size={16} />
                    </span>
                    <strong>
                      {issues.length
                        ? `${issues.length} to review`
                        : "No findings yet"}
                    </strong>
                    <small>
                      {snapshot.connections.length
                        ? "Based on collected evidence"
                        : "Enroll a NAS to begin monitoring"}
                    </small>
                  </Card>
                  <Card>
                    <span className="stat-label">
                      TELEMETRY COVERAGE <RefreshCw size={16} />
                    </span>
                    <strong>
                      {snapshot.connections.length === 0
                        ? "Not connected"
                        : coverageGaps.length || unchecked.length
                          ? "Incomplete"
                          : "Current"}
                    </strong>
                    <small>
                      {coverageGaps.length || unchecked.length
                        ? `${coverageGaps.length + unchecked.length} coverage gap(s) to review`
                        : "Freshness is assessed independently"}
                    </small>
                  </Card>
                  <Card>
                    <span className="stat-label">
                      RECOVERY READINESS <ShieldCheck size={16} />
                    </span>
                    <strong>
                      {snapshot.recovery.length
                        ? recoveryGaps
                          ? `${recoveryGaps} recovery gap(s)`
                          : "Scoped evidence recorded"
                        : "Unverified"}
                    </strong>
                    <small>Successful copies do not prove recoverability</small>
                  </Card>
                  <Card>
                    <span className="stat-label">
                      REPLACEMENT PLANS <ShoppingBag size={16} />
                    </span>
                    <strong>
                      {
                        snapshot.plans.filter((x) => x.status !== "complete")
                          .length
                      }{" "}
                      planned
                    </strong>
                    <small>Evidence-led, budget-aware decisions</small>
                  </Card>
                </div>
                <ScanOverview reports={snapshot.scanReports} />
                <div className="overview-grid">
                  <section>
                    <div className="section-title">
                      <h2>
                        Needs your attention{" "}
                        <Badge variant="secondary">{issues.length}</Badge>
                      </h2>
                      <span className="muted">Prioritized review</span>
                    </div>
                    {issues.length ? (
                      issues.map((item) => (
                        <Card className="finding" key={item.id}>
                          <Badge variant="outline">
                            {item.severity} · {item.state}
                          </Badge>
                          {item.snoozedUntilUtc && (
                            <p className="muted">
                              Delivery snoozed until{" "}
                              {date(
                                item.snoozedUntilUtc,
                                snapshot.settings.timezone,
                              )}
                              ; condition remains open.
                            </p>
                          )}
                          <h3>
                            <Link to={`/?finding=${item.id}`}>
                              {item.title}
                            </Link>
                          </h3>
                          <p>{item.evidence}</p>
                          <p className="muted">{item.nextStep}</p>
                          <Button
                            variant="outline"
                            onClick={() =>
                              void action(() =>
                                request(`/findings/${item.id}/acknowledge`, {}),
                              )
                            }
                          >
                            <Check size={15} /> Acknowledge for 7 days
                          </Button>
                          <Button
                            variant="outline"
                            onClick={() =>
                              void action(() =>
                                request(
                                  `/findings/${item.id}/${item.snoozedUntilUtc ? "unsnooze" : "snooze"}`,
                                  item.snoozedUntilUtc
                                    ? {}
                                    : {
                                        untilUtc: new Date(
                                          Date.now() + 86400000,
                                        ).toISOString(),
                                      },
                                ),
                              )
                            }
                          >
                            {item.snoozedUntilUtc
                              ? "Resume deliveries"
                              : "Snooze deliveries for 24 hours"}
                          </Button>
                          <small>
                            Critical findings bypass delivery snoozes.
                            Acknowledgment never changes NAS alerts.
                          </small>
                        </Card>
                      ))
                    ) : (
                      <Card className="empty">
                        <ShieldCheck size={32} />
                        <h3>
                          {snapshot.connections.length
                            ? "Waiting for evidence"
                            : "Meet your NAS"}
                        </h3>
                        <p>
                          Connect your NAS to see storage topology, disk
                          identity, and collection coverage. Missing data will
                          stay visibly unknown.
                        </p>
                        <Button onClick={() => setPage("Settings")}>
                          <Plus size={16} /> Connect a NAS
                        </Button>
                      </Card>
                    )}
                  </section>
                  <section>
                    <div className="section-title">
                      <h2>Plan ahead</h2>
                      <ShoppingBag size={18} />
                    </div>
                    <Card className="planning-prompt">
                      <span className="eyebrow">
                        A LITTLE PREPARATION GOES A LONG WAY
                      </span>
                      <h2>Your next drive shouldn’t be a surprise.</h2>
                      <p>
                        Keep spare stock, warranty dates, and replacement
                        budgets together. Use observed changes to decide what
                        deserves a closer look.
                      </p>
                      <Button
                        variant="outline"
                        onClick={() => setPage("Planning")}
                      >
                        Open your plan <ArrowUpRight size={16} />
                      </Button>
                      <div className="note">
                        Drive age is context, not a failure prediction.
                      </div>
                    </Card>
                  </section>
                </div>
                <div className="section-title">
                  <h2>Connected systems</h2>
                  <span className="muted">
                    {snapshot.connections.length} enrolled
                  </span>
                </div>
                <Card className="list-card">
                  {snapshot.connections.length ? (
                    snapshot.connections.map((c) => (
                      <div className="row" key={c.id}>
                        <Database />
                        <div>
                          <h3>{c.name}</h3>
                          <small>
                            Last successful collection:{" "}
                            {date(c.lastSuccessUtc, snapshot.settings.timezone)}
                          </small>
                        </div>
                        <Badge variant="outline">{c.status}</Badge>
                      </div>
                    ))
                  ) : (
                    <p className="muted">
                      No systems enrolled. Your control service is ready.
                    </p>
                  )}
                </Card>
              </>
            )}
            {page === "Storage" && (
              <StorageView
                disks={snapshot.disks}
                timezone={snapshot.settings.timezone}
                refresh={refresh}
                refreshRevision={lastRefresh}
              />
            )}
            {page === "Inventory" && <InventoryView />}
            {page === "Recovery" && (
              <RecoveryView
                timezone={snapshot.settings.timezone}
                refresh={refresh}
              />
            )}
            {page === "Planning" && (
              <PlanningReview
                timezone={snapshot.settings.timezone}
                onChanged={refresh}
                planRevision={JSON.stringify(snapshot.plans)}
              />
            )}
            {(page === "Planning" || page === "Settings") && (
              <>
                <div className="section-title">
                  <h2>
                    {page === "Settings"
                      ? "NAS connections"
                      : page === "Planning"
                        ? "Replacement & maintenance queue"
                        : "Protection policies"}
                  </h2>
                  <Dialog open={modal} onOpenChange={setModal}>
                    <DialogTrigger asChild>
                      <Button>
                        <Plus size={16} />{" "}
                        {page === "Settings"
                          ? "Connect NAS"
                          : page === "Planning"
                            ? "Add plan"
                            : "Add policy"}
                      </Button>
                    </DialogTrigger>
                    <DialogContent>
                      <DialogHeader>
                        <DialogTitle>
                          {page === "Settings"
                            ? "Enroll a NAS"
                            : page === "Planning"
                              ? "Plan ahead"
                              : "Record protection evidence"}
                        </DialogTitle>
                      </DialogHeader>
                      <form
                        onSubmit={(e) => {
                          e.preventDefault();
                          const fields = Object.fromEntries(
                            new FormData(e.currentTarget),
                          );
                          void action(() =>
                            request(
                              page === "Settings"
                                ? "/connections"
                                : page === "Planning"
                                  ? "/plans"
                                  : "/recovery",
                              {
                                ...fields,
                                ...(page === "Settings"
                                  ? {
                                      connectHost: fields.connectHost || null,
                                      connectPort: fields.connectPort
                                        ? Number(fields.connectPort)
                                        : null,
                                    }
                                  : page === "Planning"
                                    ? {
                                        budget: fields.budget
                                          ? Number(fields.budget)
                                          : null,
                                        currency: "USD",
                                      }
                                    : { rpoHours: Number(fields.rpoHours) }),
                              },
                            ),
                          );
                        }}
                      >
                        {page === "Settings" ? (
                          <>
                            <Field
                              name="name"
                              label="Display name"
                              placeholder="Home NAS"
                            />
                            <Field
                              name="apiUri"
                              label="Logical secure WebSocket address"
                              placeholder="wss://nas.home/websocket"
                            />
                            <Field
                              name="apiKey"
                              label="TrueNAS API key"
                              type="password"
                            />
                            <div className="field">
                              <Label htmlFor="enroll-trusted-ca">Trusted public CA certificate (optional)</Label>
                              <textarea id="enroll-trusted-ca" name="trustedCaPem" rows={4} maxLength={20000} aria-describedby="enroll-ca-help" />
                              <p id="enroll-ca-help" className="muted">For a private certificate, paste the public CA certificate before connecting. Never paste a private key. Leave blank for system-trusted certificates.</p>
                            </div>
                            <Field
                              name="connectHost"
                              label="Tunnel connection host (optional)"
                              required={false}
                            />
                            <Field
                              name="connectPort"
                              label="Tunnel port (optional)"
                              type="number"
                              required={false}
                            />
                            <p className="muted">
                              TLS checks the logical NAS hostname. Your existing
                              SSH tunnel only changes where the connection is
                              opened.
                            </p>
                          </>
                        ) : page === "Planning" ? (
                          <>
                            <Field
                              name="title"
                              label="What are you planning?"
                              placeholder="Purchase a compatible spare drive"
                            />
                            <Field
                              name="reason"
                              label="Evidence or reason"
                              placeholder="No spare available; review model and capacity"
                            />
                            <Field
                              name="budget"
                              label="Budget (USD)"
                              type="number"
                              required={false}
                            />
                          </>
                        ) : (
                          <>
                            <Field name="asset" label="Dataset or service" />
                            <Field
                              name="destination"
                              label="Backup destination"
                            />
                            <Field
                              name="rpoHours"
                              label="Recovery point objective (hours)"
                              type="number"
                            />
                            <Field
                              name="evidence"
                              label="Evidence / notes"
                              required={false}
                            />
                          </>
                        )}
                        <Button disabled={busy} type="submit">
                          Save
                        </Button>
                        {error && <p className="error">{error}</p>}
                      </form>
                    </DialogContent>
                  </Dialog>
                </div>
                <Card className="list-card">
                  {page === "Planning" ? (
                    snapshot.plans.length ? (
                      snapshot.plans.map((p) => (
                        <div className="row" key={p.id}>
                          <ShoppingBag />
                          <div>
                            <h3>{p.title}</h3>
                            <p className="muted">
                              Owner: {p.owner || "Unassigned"}
                            </p>
                            {p.dueUtc && (
                              <p className="muted">
                                Review by{" "}
                                {new Date(p.dueUtc).toLocaleDateString(
                                  undefined,
                                  { timeZone: snapshot.settings.timezone },
                                )}
                              </p>
                            )}
                            {p.reason.length > 300 ? (
                              <details className="plan-evidence">
                                <summary>Evidence & notes</summary>
                                <p>{p.reason}</p>
                              </details>
                            ) : (
                              <small>{p.reason}</small>
                            )}
                          </div>
                          <strong>
                            {p.budget !== null
                              ? new Intl.NumberFormat(undefined, {
                                  style: "currency",
                                  currency: p.currency,
                                }).format(p.budget)
                              : "No budget set"}
                          </strong>
                          <PlanEditor plan={p} onSaved={refresh} />
                          <select
                            aria-label={`Status for ${p.title}`}
                            value={p.status}
                            onChange={(e) =>
                              void action(() =>
                                request(
                                  `/plans/${p.id}`,
                                  { ...p, status: e.target.value },
                                  "PUT",
                                ),
                              )
                            }
                          >
                            <option value="planned">Planned</option>
                            <option value="ordered">Ordered</option>
                            <option value="complete">Complete</option>
                          </select>
                        </div>
                      ))
                    ) : (
                      <div className="empty">
                        <ShoppingBag />
                        <h3>Give future you a head start</h3>
                        <p>
                          Add a replacement, spare purchase, or maintenance
                          review with its evidence and budget.
                        </p>
                      </div>
                    )
                  ) : snapshot.connections.length ? (
                    snapshot.connections.map((c) => (
                      <div className="row" key={c.id}>
                        <Database />
                        <div>
                          <h3>{c.name}</h3>
                          <small>
                            {date(c.lastSuccessUtc, snapshot.settings.timezone)}
                          </small>
                        </div>
                        <Badge variant="outline">{c.status}</Badge>
                      </div>
                    ))
                  ) : (
                    <div className="empty">
                      <Boxes />
                      <h3>Your first connection</h3>
                      <p>
                        Connect directly to your NAS API or point to an existing
                        SSH tunnel.
                      </p>
                    </div>
                  )}
                </Card>
              </>
            )}
            {page === "Settings" && (
              <>
                <AppearanceSettings />
                <ConnectionSettings timezone={snapshot.settings.timezone} />
                <OperationsSettings
                  onSaved={(settings) => {
                    setSnapshot((current) => ({ ...current, settings }));
                    void refresh();
                  }}
                />
                <NotificationSettings />
              </>
            )}
            {page === "Activity" && <ActivityHistory />}
            <footer>
              HOMELAB CONTROL <span>Evidence first. Uncertainty visible.</span>
            </footer>
          </main>
        </div>
      </div>
    </DisplayPreferencesContext.Provider>
  );
}
