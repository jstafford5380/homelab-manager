import {useRouteDialog} from "./useRouteDialog";
import {
  RefreshServerButton,
  type ServerRefreshTarget,
} from "../components/RefreshServerButton";
import { DisplayPreferencesContext } from "../lib/displayPreferences";
import { Link, useLocation, useNavigate, useSearchParams } from "react-router";
import { lazy, Suspense, useEffect, useRef, useState } from "react";
import { Plus, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import "./App.css";
import { PlanningReview } from "../features/planning/PlanningReview";
import { StorageExportButton } from "../features/storage/StorageExportButton";
import { useAppSession } from "./useAppSession";
import { request } from "./api";
import { nav } from "./navigation";
import { LoginScreen } from "./LoginScreen";
import { ConnectionEnrollmentDialog } from "../features/settings/ConnectionEnrollmentDialog";
import { AppNavigation } from "./AppNavigation";
import { AppHeader } from "./AppHeader";
import { RefreshWarning } from "./RefreshWarning";

export default function App() {
  const {
    session,
    snapshot,
    setSnapshot,
    refresh,
    refreshPhase,
    refreshError,
    lastRefresh,
  } = useAppSession();
  const [serverRefreshTarget, setServerRefreshTarget] =
    useState<ServerRefreshTarget | null>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const location = useLocation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const page =
    (location.pathname.startsWith("/settings/")
      ? "Settings"
      : nav.find((item) => item.path === location.pathname)?.title) ??
    "Page not found";
  const findingId = page === "Overview" ? searchParams.get("finding") : null;
  const focusedPlanId = page === "Planning" ? searchParams.get("plan") : null;
  useEffect(() => {
    if (!focusedPlanId) return;
    const frame = requestAnimationFrame(() => {
      const row = document.getElementById("plan-" + focusedPlanId);
      row?.focus();
      row?.scrollIntoView({ block: "center" });
    });
    return () => cancelAnimationFrame(frame);
  }, [focusedPlanId, snapshot.plans]);
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
  const prefill=page==="Planning" && searchParams.has("hardware");
 const [modal,setModal]=useRouteDialog(location.pathname+(prefill?"?"+searchParams.toString():""),prefill);
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
  const issues = snapshot.findings.filter((x) => x.state !== "resolved");
  if (!session?.authenticated)
    return (
      <LoginScreen
        session={session}
        error={error || refreshError}
        canRetry={!!refreshError}
        busy={busy}
        onRetry={() => void refresh()}
        onSubmit={(values) =>
          void action(async () => {
            if (session?.setupRequired) await request("/setup", values);
            await request("/login", values);
          })
        }
      />
    );
  const refreshButton =
    serverRefreshTarget && serverRefreshTarget.target === page.toLowerCase() ? (
      <RefreshServerButton
        key={serverRefreshTarget.serverId}
        {...serverRefreshTarget}
        onCollected={refresh}
      />
    ) : (
      <Button variant="outline" onClick={() => void refresh()}>
        <RefreshCw size={15} /> Refresh
      </Button>
    );
  const editorDialog = (
    <ConnectionEnrollmentDialog
      open={modal}
      onOpenChange={setModal}
      busy={busy}
      error={error}
      onSubmit={(fields) => void action(() => request("/connections", fields))}
    />
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
        <AppNavigation
          page={page}
          issueCount={issues.length}
          onLogout={() => void action(() => request("/logout", {}))}
        />
        <div className="main">
          <AppHeader page={page} refreshPhase={refreshPhase} />
          <main id="main-content" tabIndex={-1}>
            {refreshError && (
              <RefreshWarning
                refreshError={refreshError}
                lastRefresh={lastRefresh}
                timezone={snapshot.settings.timezone}
                refresh={refresh}
              />
            )}
            <div
              className={
                page === "Storage"
                  ? "page-heading storage-page-heading"
                  : page === "Planning"
                    ? "page-heading planning-page-heading"
                    : "page-heading"
              }
            >
              <div>
                <p className="eyebrow">HOMELAB CONTROL</p>
                <h1 ref={headingRef} tabIndex={-1}>
                  {findingId
                    ? "Finding evidence"
                    : page === "Overview"
                      ? "Your homelab, at a glance."
                      : page}
                </h1>
                <p
                  className={page === "Storage" ? "storage-subtitle" : "muted"}
                >
                  {findingId
                    ? "Recorded evidence, source details, and review history."
                    : page === "Overview"
                      ? "What needs attention today, and what’s coming up."
                      : page === "Hardware"
                        ? "Components and how they’re running, one server at a time."
                        : page === "Planning"
                          ? "Turn observations into a practical replacement plan."
                          : page === "Storage"
                            ? "Pools and drives, one server at a time."
                            : page === "Settings"
                              ? "How Homelab Control reads your systems, what it flags for review, and where it sends alerts."
                              : "Evidence, history, and the details that matter."}
                </p>
              </div>
              {page === "Storage" ? (
                <div className="storage-heading-actions">
                  <StorageExportButton />
                  {refreshButton}
                </div>
              ) : page === "Planning" ? (
                <Button onClick={() => setModal(true)}>
                  <Plus size={16} />
                  Add plan
                </Button>
              ) : (
                refreshButton
              )}
            </div>
            {error && (
              <div className="error" role="alert">
                {error}
              </div>
            )}
            <Suspense fallback={<p role="status">Loading {page.toLowerCase()}…</p>}>
            {page === "Hardware" && (
              <Suspense fallback={<p role="status">Loading hardware…</p>}>
                <HardwareView
                  refreshRevision={lastRefresh}
                  onRefreshTargetChange={setServerRefreshTarget}
                />
              </Suspense>
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
              <OverviewView
                snapshot={snapshot}
                refreshRevision={lastRefresh}
                busy={busy}
                onFindingAction={(path, body) =>
                  void action(() => request(path, body))
                }
                onConnect={() => setPage("Settings")}
              />
            )}
            {page === "Storage" && (
              <StorageView
                disks={snapshot.disks}
                timezone={snapshot.settings.timezone}
                refresh={refresh}
                refreshRevision={lastRefresh}
                onRefreshTargetChange={setServerRefreshTarget}
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
                onChanged={async (saved) => {
                  if (saved?.id && saved.title && saved.status)
                    setSnapshot((current) => ({
                      ...current,
                      plans: [
                        ...current.plans.filter((plan) => plan.id !== saved.id),
                        saved,
                      ],
                    }));
                  await refresh();
                }}
                plans={snapshot.plans}
                disks={snapshot.disks}
                createOpen={modal}
                onCreateOpenChange={setModal}
                initialTitle={searchParams.get("title") ?? ""}
                initialNotes={searchParams.get("reason") ?? ""}
              />
            )}
            {page === "Settings" && (
              <SettingsPage
                enrollment={editorDialog}
                connectionVersion={JSON.stringify(snapshot.connections)}
                onDisplaySaved={(settings) =>
                  setSnapshot((current) => ({ ...current, settings }))
                }
              />
            )}
            {page === "Activity" && <ActivityHistory />}
            </Suspense>
            <footer>
              HOMELAB CONTROL <span>Evidence first. Uncertainty visible.</span>
            </footer>
          </main>
        </div>
      </div>
    </DisplayPreferencesContext.Provider>
  );
}

const HardwareView = lazy(() =>
  import("../features/hardware/HardwareView").then((m) => ({
    default: m.HardwareView,
  })),
);

const SettingsPage=lazy(()=>import("../features/settings/SettingsPage").then(m=>({default:m.SettingsPage})));

const ActivityHistory=lazy(()=>import("../features/activity/ActivityHistory").then(m=>({default:m.ActivityHistory})));

const OverviewView=lazy(()=>import("../features/overview/OverviewView").then(m=>({default:m.OverviewView})));

const FindingDetail=lazy(()=>import("../features/findings/FindingDetail").then(m=>({default:m.FindingDetail})));

const InventoryView=lazy(()=>import("../features/inventory/InventoryView").then(m=>({default:m.InventoryView})));

const RecoveryView=lazy(()=>import("../features/recovery/RecoveryView").then(m=>({default:m.RecoveryView})));

const StorageView=lazy(()=>import("../features/storage/StorageView").then(m=>({default:m.StorageView})));
