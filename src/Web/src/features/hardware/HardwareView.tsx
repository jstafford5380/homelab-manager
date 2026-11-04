import type { ServerRefreshTarget } from "../../components/RefreshServerButton";
import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { Plus } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { HardwarePill } from "./HardwareCharts";
import { HardwareSurface } from "./HardwareSurface";
import { HardwareReadingForm } from "./HardwareReadingForm";
import { HardwareComponentForm } from "./HardwareComponentForm";
import { gb, hardwareRequest, number, type HwReading } from "./hardwareModel";
import { relative } from "../storage/storageModel";
import { useDisplayFormat } from "../../lib/displayPreferences";
import "./Hardware.css";
import { HardwarePanel } from "./HardwarePanel";
import { HardwareLoading } from "./HardwareLoading";
import { HardwareInventory } from "./HardwareInventory";
import { HardwareMetrics } from "./HardwareMetrics";
import { HardwareServerFacts } from "./HardwareServerFacts";
import { useHardwareData } from "./useHardwareData";

export function HardwareView({
  refreshRevision,
  onRefreshTargetChange,
}: {
  refreshRevision?: number | null;
  onRefreshTargetChange: (target: ServerRefreshTarget | null) => void;
}) {
  const [params, setParams] = useSearchParams(),
    selected = params.get("server"),
    componentId = params.get("component");
  const [range, setRange] = useState("30d");
  const { list, detail, error, loaded, serverId, retry, setError } =
    useHardwareData(selected, range, refreshRevision);
  const [message, setMessage] = useState("");
  const [form, setForm] = useState<
      "reading" | "component" | "edit" | "replace" | "note" | null
    >(null),
    [reading, setReading] = useState<HwReading | undefined>();
  const [returnFocus, setReturnFocus] = useState<HTMLElement | null>(null),
    [panelFocus, setPanelFocus] = useState<HTMLElement | null>(null);
  const [asOf] = useState(Date.now);
  const [editComponentId, setEditComponentId] = useState<string>();
  const [setupUpsKey, setSetupUpsKey] = useState<string>();
  const [formError, setFormError] = useState(""),
    [savingNote, setSavingNote] = useState(false);
  const { time } = useDisplayFormat();
  useEffect(() => {
    if (!selected && list?.servers[0])
      setParams({ server: list.servers[0].id }, { replace: true });
  }, [selected, list, setParams]);
  // Server identity is checked before rendering cached data.
  const d = detail?.server.id === serverId ? detail : null;
  const refreshServerId = d?.server.id;
  const refreshFreshness = d?.server.freshness;
  useEffect(() => {
    onRefreshTargetChange(
      refreshServerId &&
        refreshFreshness !== "Paused" &&
        refreshFreshness !== "Not connected"
        ? { serverId: refreshServerId, target: "hardware" }
        : null,
    );
    return () => onRefreshTargetChange(null);
  }, [onRefreshTargetChange, refreshServerId, refreshFreshness]);
  const rangePending =
    !!d &&
    d.coverage.days !== (range === "24h" ? 1 : Number(range.slice(0, -1)));
  const component = d
    ? [...d.components, ...(d.retiredComponents ?? [])].find(
        (c) => c.record.id === componentId,
      )
    : undefined;
  const formComponent = editComponentId
    ? d?.components.find((part) => part.record.id === editComponentId)
    : component;
  const openForm = (value: typeof form, r?: HwReading) => {
    setReturnFocus(document.activeElement as HTMLElement);
    setReading(r);
    setSetupUpsKey(undefined);
    setEditComponentId(undefined);
    setFormError("");
    setForm(value);
  };
  const editComponent = (id: string) => {
    openForm("edit");
    setEditComponentId(id);
  };
  const closeForm = () => {
    setForm(null);
    setReading(undefined);
  };
  const saved = (id?: string) => {
    closeForm();
    setMessage("Saved");
    if (id && componentId) setParams({ server: serverId!, component: id });
    retry();
  };
  const closePanel = () => {
    setParams({ server: serverId! });
  };
  const stale =
    !!d &&
    (!["Reporting", "Not connected", "No collection yet"].includes(
      d.server.freshness,
    ) ||
      !!error);
  const rangeLabel =
    d?.coverage.days === 1 ? "24 hours" : `${d?.coverage.days ?? 30} days`;
  const noAutomatic =
    (!list?.hwmetrics && d?.server.platform !== "synology") ||
    d?.server.freshness === "Not connected";
  return (
    <div className="hardware-page">
      <p className="sr-only" role="status">
        {component || form ? "" : message}
      </p>
      {error && (
        <div className="hw-banner hw-bad" role="status">
          <strong>Hardware couldn’t be loaded.</strong> {error}{" "}
          {d && loaded
            ? `Showing what was loaded ${relative(loaded)}; current status is unknown.`
            : "Nothing has been loaded yet."}{" "}
          <Button variant="outline" onClick={() => retry()}>
            Retry
          </Button>
        </div>
      )}
      {!list && !error && <HardwareLoading />}
      {list && list.servers.length === 0 && (
        <Card className="empty">
          <h2>No servers yet</h2>
          <p>
            Connect a TrueNAS{list.synology ? " or Synology" : ""} server in
            Settings and its processor, memory and system details appear here.
          </p>
          <Button asChild>
            <Link to="/settings">Go to Settings</Link>
          </Button>
        </Card>
      )}
      {!!list?.servers.length && (
        <>
          <div className="hw-tabs" role="group" aria-label="Servers">
            {list.servers.map((s) => (
              <button
                key={s.id}
                aria-pressed={s.id === serverId}
                onClick={() => {
                  closeForm();
                  setParams({ server: s.id });
                }}
              >
                <span
                  className={`hw-server-dot ${s.freshness === "Reporting" ? "hw-dot-ok" : "hw-dot-warn"}`}
                />
                <span>
                  <strong>{s.name}</strong>
                  <small>
                    {s.freshness === "Reporting"
                      ? `${s.componentCount} components${s.watchCount ? ` · ${s.watchCount} to watch` : ""}${s.nearLimitCount ? ` · ${s.nearLimitCount} near its limit` : ""}`
                      : s.freshness}
                  </small>
                </span>
              </button>
            ))}
          </div>
          {!d && !error && <HardwareLoading />}
          {d && (
            <>
              {rangePending && !error && (
                <p role="status">
                  Loading the selected time range… Showing the last loaded range
                  until it arrives.
                </p>
              )}
              {stale && !error && (
                <div className="hw-banner hw-warn" role="status">
                  <strong>Can’t reach this server right now.</strong> Everything
                  below is from the last successful check,{" "}
                  {relative(d.server.lastSuccessUtc)}. Averages stop at that
                  point, and component status may have changed since.
                </div>
              )}
              {d.server.freshness === "Not connected" && (
                <div className="hw-banner hw-info" role="status">
                  <strong>
                    This server isn’t connected, so its readings are entered by
                    hand.
                  </strong>{" "}
                  Averages come only from the readings you add and can miss busy
                  or quiet periods. Add a reading or{" "}
                  <Link
                    to={`/settings/connections?server=${d.server.id}#synology`}
                  >
                    connect it in Settings
                  </Link>{" "}
                  to collect automatically.
                </div>
              )}
              {!stale &&
                !noAutomatic &&
                [d.cpu, d.memory, d.temperature].some((m) => m.partial) && (
                  <div className="hw-banner hw-info" role="status">
                    <strong>
                      Collecting since{" "}
                      {relative(d.coverage.collectionStartedUtc)}.
                    </strong>{" "}
                    Each average appears once its whole range has been sampled:
                    24 hours tomorrow, 30 days in a month. Until then, cards
                    show the latest sample.
                  </div>
                )}
              <header className="hw-server-header">
                <div>
                  <div className="hw-card-heading">
                    <h2>{d.server.name}</h2>
                    <HardwarePill
                      status={{
                        label: stale ? "Out of date" : d.server.freshness,
                        tone: stale
                          ? "neutral"
                          : d.server.freshness === "Reporting"
                            ? "ok"
                            : "info",
                        why: "",
                      }}
                    />
                    <span className="hw-pill hw-info">
                      {d.server.freshness === "Not connected"
                        ? "Entered by hand"
                        : noAutomatic
                          ? "Inventory detected"
                          : "Collected automatically"}
                    </span>
                  </div>
                  <p>
                    {d.server.host} ·{" "}
                    {d.server.platform === "truenas"
                      ? "TrueNAS"
                      : "Synology DSM"}{" "}
                    {d.server.version ?? "Version unknown"} · inventory checked{" "}
                    {relative(d.server.lastSuccessUtc)}
                  </p>
                </div>
                <div className="hw-actions">
                  <Button variant="outline" onClick={() => openForm("reading")}>
                    <Plus size={15} /> Add reading
                  </Button>
                  <Button
                    variant="outline"
                    onClick={() => openForm("component")}
                  >
                    <Plus size={15} /> Add component
                  </Button>
                </div>
              </header>
              <HardwareServerFacts
                d={d}
                stale={stale}
                rangeLabel={rangeLabel}
              />
              <HardwareMetrics
                d={d}
                range={range}
                stale={stale}
                setRange={setRange}
                onReading={() => openForm("reading")}
                onSetupUps={(key) => {
                  openForm("component");
                  setSetupUpsKey(key);
                }}
                onEdit={editComponent}
              />

              <HardwareInventory
                d={d}
                stale={!!error}
                asOf={asOf}
                onEdit={editComponent}
                onOpen={(id, opener) => {
                  setPanelFocus(opener);
                  setParams({ server: d.server.id, component: id });
                }}
              />

              {!!d.retiredComponents?.length && (
                <details>
                  <summary>
                    Replaced components ({d.retiredComponents.length})
                  </summary>
                  {d.retiredComponents.map((c) => (
                    <p key={c.record.id}>
                      <Button
                        variant="link"
                        onClick={(e) => {
                          setPanelFocus(e.currentTarget);
                          setParams({
                            server: d.server.id,
                            component: c.record.id,
                          });
                        }}
                      >
                        {c.title} · replaced {time(c.record.retiredUtc)}
                      </Button>
                    </p>
                  ))}
                </details>
              )}
              <details className="hw-readings">
                <summary>Entered readings ({d.readings.length})</summary>
                {d.readings.map((r) => (
                  <article key={r.id}>
                    <p>
                      {time(r.takenUtc)} · entered by you ·{" "}
                      {r.kind === "average"
                        ? `Average over ${r.periodMinutes} minutes`
                        : "A moment in time"}
                    </p>
                    <p>
                      CPU {number(r.cpuPercent, "%")} · Memory{" "}
                      {gb(r.memoryUsedBytes)} · Temperature{" "}
                      {number(r.temperatureC, " °C")} · Power{" "}
                      {number(r.powerWatts, " W")}
                    </p>
                    {r.note && <p>{r.note}</p>}
                    <Button
                      variant="outline"
                      onClick={() => openForm("reading", r)}
                    >
                      Edit reading
                    </Button>
                    <Button
                      variant="ghost"
                      onClick={async () => {
                        try {
                          await hardwareRequest(
                            `/servers/${d.server.id}/readings/${r.id}?revision=${r.revision}`,
                            undefined,
                            "DELETE",
                          );
                          saved();
                        } catch (err) {
                          setError(
                            err instanceof Error ? err.message : String(err),
                          );
                        }
                      }}
                    >
                      Delete reading
                    </Button>
                  </article>
                ))}
              </details>
              {component && (
                <HardwareSurface
                  title={component.title}
                  panel
                  close={closePanel}
                  returnFocus={panelFocus}
                >
                  <p role="status">{message}</p>
                  <HardwarePanel
                    component={component}
                    detail={d}
                    time={time}
                    openForm={openForm}
                    stale={stale}
                  />
                </HardwareSurface>
              )}
              {componentId && !component && !form && (
                <p role="status">
                  This component isn’t in the active inventory.{" "}
                  <Button variant="link" onClick={closePanel}>
                    Return to components
                  </Button>
                </p>
              )}
              {form && (
                <HardwareSurface
                  title={
                    form === "reading"
                      ? `${reading ? "Edit" : "Add"} a reading for ${d.server.name}`
                      : form === "note"
                        ? "Add note"
                        : form === "replace"
                          ? `Replace ${component?.title}`
                          : form === "edit"
                            ? `Edit ${formComponent?.title}`
                            : setupUpsKey
                              ? "Set up this UPS"
                              : `Add a component to ${d.server.name}`
                  }
                  description={
                    form === "reading"
                      ? "Copy these from the server’s own dashboard, a power meter or your UPS. Leave blank anything you didn’t measure."
                      : setupUpsKey
                        ? "Link the detected UPS and enter the values used for power estimates."
                        : form === "component"
                          ? "For parts the app can’t detect, like power supplies, coolers and cards. Detected parts appear on their own."
                          : undefined
                  }
                  close={closeForm}
                  returnFocus={returnFocus}
                >
                  {form === "reading" ? (
                    <HardwareReadingForm
                      detail={d}
                      reading={reading}
                      saved={saved}
                      cancel={closeForm}
                    />
                  ) : form === "note" && component ? (
                    <form
                      onSubmit={async (e) => {
                        e.preventDefault();
                        const text = new FormData(e.currentTarget).get("note");
                        setSavingNote(true);
                        try {
                          await hardwareRequest(
                            `/servers/${d.server.id}/components/${component.record.id}/notes`,
                            { text },
                          );
                          saved();
                        } catch (err) {
                          setFormError(
                            err instanceof Error ? err.message : String(err),
                          );
                        } finally {
                          setSavingNote(false);
                        }
                      }}
                    >
                      <label htmlFor="hw-history-note">Note</label>
                      <textarea
                        id="hw-history-note"
                        name="note"
                        maxLength={2000}
                        required
                      />
                      {formError && <p role="alert">{formError}</p>}
                      <Button disabled={savingNote}>Save note</Button>
                    </form>
                  ) : (
                    <HardwareComponentForm
                      detail={d}
                      component={
                        form === "component" ? undefined : formComponent
                      }
                      replace={form === "replace"}
                      setupUpsKey={setupUpsKey}
                      saved={saved}
                      cancel={closeForm}
                    />
                  )}
                </HardwareSurface>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
