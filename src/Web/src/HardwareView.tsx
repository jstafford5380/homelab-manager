import type { ServerRefreshTarget } from "./RefreshServerButton";
import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { Plus } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import {
  HardwareChart,
  HardwareMetricCard,
  HardwarePill,
  HardwareReviewLine,
} from "./HardwareCharts";
import {
  HardwareComponentForm,
  HardwareReadingForm,
  HardwareSurface,
} from "./HardwareForms";
import {
  gb,
  hardwareRequest,
  hwKinds,
  number,
  type HwComponent,
  type HwDetail,
  type HwReading,
  type HwServer,
} from "./hardwareModel";
import { relative } from "./storageModel";
import { useDisplayFormat } from "./displayPreferences";
import "./Hardware.css";
type List = { hwmetrics: boolean; synology: boolean; servers: HwServer[] };
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
  const [list, setList] = useState<List | null>(null),
    [detail, setDetail] = useState<HwDetail | null>(null),
    [range, setRange] = useState("30d");
  const [error, setError] = useState(""),
    [retry, setRetry] = useState(0),
    [loaded, setLoaded] = useState<string | null>(null),
    [message, setMessage] = useState("");
  const [form, setForm] = useState<
      "reading" | "component" | "edit" | "replace" | "note" | null
    >(null),
    [reading, setReading] = useState<HwReading | undefined>();
  const [returnFocus, setReturnFocus] = useState<HTMLElement | null>(null),
    [panelFocus, setPanelFocus] = useState<HTMLElement | null>(null);
  const [asOf] = useState(Date.now);
  const [editUpsId, setEditUpsId] = useState<string>();
  const [setupUpsKey, setSetupUpsKey] = useState<string>();
  const [formError, setFormError] = useState(""),
    [savingNote, setSavingNote] = useState(false);
  const { time } = useDisplayFormat();
  useEffect(() => {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort("timeout"), 15000);
    void hardwareRequest("/servers", undefined, "GET", controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) {
          setList(data);
        }
      })
      .catch((e) => {
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setError(e.message);
      })
      .finally(() => clearTimeout(timeout));
    return () => {
      clearTimeout(timeout);
      controller.abort();
    };
  }, [retry, refreshRevision]);
  useEffect(() => {
    if (!selected && list?.servers[0])
      setParams({ server: list.servers[0].id }, { replace: true });
  }, [selected, list, setParams]);
  const serverId = selected ?? list?.servers[0]?.id;
  useEffect(() => {
    if (!serverId) return;
    const controller = new AbortController(),
      timeout = setTimeout(() => controller.abort("timeout"), 15000);
    void hardwareRequest(
      `/servers/${serverId}?range=${range}`,
      undefined,
      "GET",
      controller.signal,
    )
      .then((data) => {
        if (!controller.signal.aborted) {
          setError("");
          setDetail(data);
          setLoaded(new Date().toISOString());
        }
      })
      .catch((e) => {
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setError(e.message);
      })
      .finally(() => clearTimeout(timeout));
    return () => {
      clearTimeout(timeout);
      controller.abort();
    };
  }, [serverId, range, retry, refreshRevision]);
  // Server identity is checked before rendering cached data.
  const d = detail?.server.id === serverId ? detail : null;
  const refreshServerId = d?.server.id;
  const refreshFreshness = d?.server.freshness;
  useEffect(() => {
    onRefreshTargetChange(refreshServerId && refreshFreshness !== "Paused" && refreshFreshness !== "Not connected"
      ? { serverId: refreshServerId, target: "hardware" }
      : null);
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
  const formComponent = editUpsId ? d?.components.find(part => part.record.id === editUpsId) : component;
  const openForm = (value: typeof form, r?: HwReading) => {
    setReturnFocus(document.activeElement as HTMLElement);
    setReading(r);
    setSetupUpsKey(undefined);
    setEditUpsId(undefined);
    setFormError("");
    setForm(value);
  };
  const closeForm = () => {
    setForm(null);
    setReading(undefined);
  };
  const saved = (id?: string) => {
    closeForm();
    setMessage("Saved");
    if (id && componentId) setParams({ server: serverId!, component: id });
    setRetry((n) => n + 1);
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
          <Button variant="outline" onClick={() => setRetry((n) => n + 1)}>
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
                  <Link to={`/settings/connections?server=${d.server.id}#synology`}>
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
              <dl className="hw-facts">
                {[
                  [
                    d.cpu.source === "entered"
                      ? "CPU · avg of readings"
                      : `CPU · ${rangeLabel}`,
                    d.cpu.source === "none"
                      ? "No readings"
                      : d.cpu.partial
                        ? "Collecting"
                        : number(d.cpu.average, "%"),
                    `${stale ? "Last known · " : ""}${d.cpu.source === "entered" ? "Entered by you" : `Latest ${number(d.cpu.latest, "%")}`}`,
                  ],
                  [
                    "Memory used by apps",
                    d.memory.source === "none"
                      ? "No readings"
                      : d.memory.partial
                        ? "Collecting"
                        : gb(d.memory.average),
                    `${stale ? "Last known · " : ""}of ${gb(d.totalBytes)}`,
                  ],
                  d.server.freshness === "Not connected"
                    ? [
                        "Readings",
                        String(d.readings.length),
                        `In the last ${rangeLabel}`,
                      ]
                    : [
                        "Uptime",
                        d.server.uptimeSeconds == null
                          ? "Unknown"
                          : `${Math.floor(d.server.uptimeSeconds / 86400)} days`,
                        stale ? "Last known" : "From server inventory",
                      ],
                  [
                    "Needs a look",
                    stale || d.server.watchCount == null
                      ? "Unknown"
                      : String(
                          d.server.watchCount + (d.server.nearLimitCount ?? 0),
                        ),
                    stale ? "Server out of date" : "Components",
                  ],
                ].map(([label, value, caption]) => (
                  <Card key={label}>
                    <dt>{label}</dt>
                    <dd>
                      {value}
                      <small>{caption}</small>
                    </dd>
                  </Card>
                ))}
              </dl>
              <section aria-labelledby="hw-running">
                <div className="hw-section-heading">
                  <div>
                    <h2 id="hw-running">How it’s running</h2>
                    <p className="hw-coverage">
                      {d.lastCollectionUtc
                        ? `Last collection run · ${time(d.lastCollectionUtc)}`
                        : "No collection run recorded"}
                    </p>
                  </div>
                  <div
                    className="hw-ranges"
                    role="group"
                    aria-label="Time range"
                  >
                    {[
                      ["24h", "24 h"],
                      ["7d", "7 days"],
                      ["30d", "30 days"],
                      ["90d", "90 days"],
                    ].map(([key, label]) => (
                      <button
                        key={key}
                        aria-pressed={range === key}
                        onClick={() => setRange(key)}
                      >
                        {label}
                      </button>
                    ))}
                  </div>
                </div>
                <div className="hw-metrics">
                  {(["cpu", "memory", "temperature", "power"] as const).map(
                    (type) => (
                      <HardwareMetricCard
                        key={type}
                        title={
                          type === "cpu"
                            ? "CPU use"
                            : type === "memory"
                              ? "Memory"
                              : type === "power"
                                ? "Power draw"
                                : d.server.platform === "synology"
                                  ? "System temperature"
                                  : "CPU temperature"
                        }
                        metric={d[type]}
                        detail={d}
                        type={type}
                        stale={stale}
                        onReading={() => openForm("reading")}
                        onSetupUps={key => { openForm("component"); setSetupUpsKey(key); }}
                        onEditUps={id => { openForm("edit"); setEditUpsId(id); }}
                      />
                    ),
                  )}
                </div>
                <HardwareReviewLine detail={d} />
              </section>
              <section aria-labelledby="hw-components">
                <h2 id="hw-components">Components</h2>
                <p className="hw-coverage">
                  {d.components.length} components ·{" "}
                  {
                    d.components.filter((c) => c.source !== "You entered")
                      .length
                  }{" "}
                  detected,{" "}
                  {d.components.filter((c) => c.source !== "Detected").length}{" "}
                  entered by you
                  {d.server.watchCount
                    ? ` · ${d.server.watchCount} to watch`
                    : ""}
                  {d.server.nearLimitCount
                    ? ` · ${d.server.nearLimitCount} near its limit`
                    : ""}
                </p>
                <Card className="hw-inventory">
                  {["Compute", "Power & cooling", "Expansion & chassis"].map(
                    (group) => {
                      const items = d.components.filter(
                        (c) => c.group === group,
                      );
                      return items.length ? (
                        <section key={group}>
                          <h3>{group}</h3>
                          <table>
                            <thead>
                              <tr>
                                {[
                                  "Status",
                                  "Component",
                                  "Details",
                                  "Source",
                                  "In service",
                                ].map((h) => (
                                  <th key={h}>{h}</th>
                                ))}
                              </tr>
                            </thead>
                            <tbody>
                              {items.map((c) => (
                                <tr key={c.record.id}>
                                  <td>
                                    <HardwarePill
                                      status={c.status}
                                      stale={!!error}
                                    />
                                  </td>
                                  <td>
                                    <button
                                      className="hw-component-name"
                                      onClick={(e) => {
                                        setPanelFocus(e.currentTarget);
                                        setParams({
                                          server: d.server.id,
                                          component: c.record.id,
                                        });
                                      }}
                                    >
                                      {c.title}
                                    </button>
                                    <small>{hwKinds[c.record.kind]}</small>
                                  </td>
                                  <td>
                                    {c.record.detectedSpec && (
                                      <span>
                                        {c.record.detectedSpec}{" "}
                                        <small className="hw-source">
                                          Detected
                                        </small>
                                      </span>
                                    )}
                                    {c.record.spec && (
                                      <span>
                                        {c.record.spec}{" "}
                                        <small>You entered</small>
                                      </span>
                                    )}
                                    {!c.record.detectedSpec &&
                                      !c.record.spec &&
                                      "Not recorded"}
                                  </td>
                                  <td
                                    className={
                                      c.source !== "You entered"
                                        ? "hw-source"
                                        : ""
                                    }
                                  >
                                    {c.source}
                                  </td>
                                  <td>
                                    {c.record.installedUtc
                                      ? `${Math.max(0, Math.floor((asOf - new Date(c.record.installedUtc).getTime()) / 86400000 / 30.44))} months${c.record.datesEstimated ? " · estimated" : ""}`
                                      : "Not recorded"}
                                  </td>
                                </tr>
                              ))}
                            </tbody>
                          </table>
                        </section>
                      ) : null;
                    },
                  )}
                  {!d.components.length && (
                    <p className="hw-empty-components">
                      No components yet. Detected parts appear after collection,
                      or add a component.
                    </p>
                  )}
                </Card>
              </section>
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
                            : setupUpsKey ? "Set up this UPS" : `Add a component to ${d.server.name}`
                  }
                  description={
                    form === "reading"
                      ? "Copy these from the server’s own dashboard, a power meter or your UPS. Leave blank anything you didn’t measure."
                      : setupUpsKey ? "Link the detected UPS and enter the values used for power estimates."
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
                      component={form === "component" ? undefined : formComponent}
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
function HardwareLoading() {
  return (
    <div
      role="group"
      aria-label="Loading hardware"
      aria-busy="true"
      className="hw-loading"
    >
      <div className="hw-loading-tabs" />
      <div className="hw-facts">
        {[1, 2, 3, 4].map((n) => (
          <div key={n} />
        ))}
      </div>
      <div className="hw-metrics">
        {[1, 2, 3, 4].map((n) => (
          <div key={n} />
        ))}
      </div>
      <div className="hw-loading-inventory" />
    </div>
  );
}
function HardwarePanel({
  component: c,
  detail: d,
  time,
  openForm,
  stale,
}: {
  component: HwComponent;
  detail: HwDetail;
  time: (s: string | null) => string;
  openForm: (type: "edit" | "replace" | "note") => void;
  stale: boolean;
}) {
  const [asOf] = useState(Date.now);
  const r = c.record,
    metric = r.retiredUtc
      ? null
      : (c.use ??
        c.heat ??
        (r.kind === "memory"
          ? d.memory
          : r.kind === "processor"
            ? d.cpu
            : r.kind === "nas-unit" && d.server.platform === "synology"
              ? d.temperature
              : null));
  const used = c.slots.filter((s) => s.sizeBytes != null).length;
  return (
    <div className="hw-panel-body">
      <p>
        {hwKinds[r.kind]} · {d.server.name}
      </p>
      <HardwarePill status={c.status} stale={stale} />
      {["Watch", "Act now", "Near its limit"].some(
        (s) => c.status.label.includes(s.toLowerCase()) || c.status.label === s,
      ) && (
        <div className={`hw-banner hw-${c.status.tone}`}>
          <strong>Why “{c.status.label}”:</strong> {c.status.why}
        </div>
      )}
      <section>
        <h3>How it’s running</h3>
        {metric && metric.source !== "none" ? (
          <>
            <p>
              {number(metric.average ?? metric.latest)} ·{" "}
              {metric.source === "entered"
                ? "entered by you"
                : metric.partial
                  ? "Latest sample"
                  : `${d.coverage.days}-day average`}
            </p>
            <HardwareChart
              metric={metric}
              title={hwKinds[r.kind]}
              days={d.coverage.days}
              total={r.kind === "memory" ? d.totalBytes : null}
              endUtc={stale ? d.server.lastSuccessUtc : null}
            />
            <p>The dashed line is your review level.</p>
            {r.kind === "processor" &&
              (c.heat ?? d.temperature).source !== "none" &&
              d.server.platform === "truenas" && (
                <>
                  <p>
                    CPU temperature ·{" "}
                    {number(
                      (c.heat ?? d.temperature).average ??
                        (c.heat ?? d.temperature).latest,
                      " °C",
                    )}{" "}
                    · hottest {number((c.heat ?? d.temperature).peak, " °C")}
                  </p>
                  <HardwareChart
                    metric={c.heat ?? d.temperature}
                    title="CPU temperature"
                    days={d.coverage.days}
                    endUtc={stale ? d.server.lastSuccessUtc : null}
                  />
                </>
              )}
          </>
        ) : (
          <p>{c.status.why}</p>
        )}
      </section>
      {r.kind === "memory" && (
        <section>
          <h3>Memory slots</h3>
          <div
            className="hw-slots"
            role="img"
            aria-label={`${used} of ${c.slots.length} slots used · ${gb(c.enteredTotalBytes)} installed · board supports up to ${gb(r.maxSupportedBytes)} · You entered`}
          >
            {c.slots.map((s, i) => (
              <div key={i} className={s.sizeBytes ? "hw-slot-used" : ""}>
                <strong>{s.name}</strong>
                <span>{s.sizeBytes ? gb(s.sizeBytes) : "Empty"}</span>
              </div>
            ))}
          </div>
          <p>
            {c.slots.length
              ? `${used} of ${c.slots.length} slots used · ${gb(c.enteredTotalBytes)} installed`
              : "Slots not entered"}
            {r.solderedBytes != null
              ? ` · ${gb(r.solderedBytes)} soldered to the board`
              : ""}{" "}
            · board supports up to {gb(r.maxSupportedBytes)} (you entered).
          </p>
          {c.slots.length > used && (
            <p>
              Room for {c.slots.length - used} more modules. Check the board’s
              channel and module compatibility requirements.
            </p>
          )}
          {c.slots.length > 0 && used === c.slots.length && (
            <p>
              No free slots.
              {r.maxSupportedBytes
                ? ` Reaching ${gb(r.maxSupportedBytes)} means replacing modules; confirm supported sizes with the manufacturer.`
                : ""}
            </p>
          )}
          {r.detectedTotalBytes != null && c.enteredTotalBytes != null && (
            <p
              className={
                r.detectedTotalBytes !== c.enteredTotalBytes
                  ? "hw-banner hw-bad"
                  : ""
              }
            >
              {r.detectedTotalBytes === c.enteredTotalBytes
                ? "The detected total matches the modules you entered."
                : `Detected ${gb(r.detectedTotalBytes)}, but you entered ${gb(c.enteredTotalBytes)}. Reported usable memory can be below nominal module capacity because of kernel or firmware reservations. Check the entered inventory and installed modules.`}
            </p>
          )}
        </section>
      )}
      <section>
        <h3>Details</h3>
        <dl className="hw-details">
          {[
            [r.detectedName, "Model", "Detected"],
            [r.detectedSpec, "Detected specs", "Detected"],
            [r.model, "Model or part number", "You entered"],
            [r.spec, "Key specs", "You entered"],
            [r.notes, "Notes", "You entered"],
          ]
            .filter(([value]) => value)
            .map(([value, label, source]) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>
                  {value}
                  <small className={source === "Detected" ? "hw-source" : ""}>
                    {source}
                  </small>
                </dd>
              </div>
            ))}
        </dl>
      </section>
      <section>
        <h3>Purchase</h3>
        <p className="muted">You entered</p>
        <dl className="hw-details">
          <div>
            <dt>Price paid</dt>
            <dd>
              {r.pricePaid == null
                ? "Not recorded"
                : `$${r.pricePaid.toLocaleString()} USD${r.installedUtc && asOf - new Date(r.installedUtc).getTime() > 86400000 ? ` · about $${Math.round(r.pricePaid / ((asOf - new Date(r.installedUtc).getTime()) / 31557600000))} per year so far` : ""}`}
            </dd>
          </div>
          <div>
            <dt>Purchased</dt>
            <dd>
              {r.purchasedUtc
                ? new Date(r.purchasedUtc).toLocaleDateString(undefined, {
                    timeZone: "UTC",
                  })
                : "Not recorded"}
            </dd>
          </div>
          <div>
            <dt>Installed</dt>
            <dd>
              {r.installedUtc
                ? new Date(r.installedUtc).toLocaleDateString(undefined, {
                    timeZone: "UTC",
                  })
                : "Not recorded"}
            </dd>
          </div>
          <div>
            <dt>Warranty</dt>
            <dd>
              {r.warrantyEndUtc
                ? new Date(r.warrantyEndUtc).toLocaleDateString(undefined, {
                    timeZone: "UTC",
                  })
                : "Not recorded"}
            </dd>
          </div>
        </dl>
        {r.datesEstimated && <p>These dates are estimates · You entered</p>}
      </section>
      <section>
        <h3>History</h3>
        {c.history.map((h, i) => (
          <p key={i}>
            <small>{time(h.atUtc)}</small> {h.text}
          </p>
        ))}
        {!c.history.length && <p>No recorded history yet.</p>}
        <Button variant="outline" onClick={() => openForm("note")}>
          Add note
        </Button>
      </section>
      {!r.retiredUtc && (
        <div className="hw-actions">
          <Button variant="outline" onClick={() => openForm("edit")}>
            Edit details
          </Button>
          <Button variant="outline" asChild>
            <Link
              to={`/planning?hardware=${r.id}&title=${encodeURIComponent("Upgrade " + c.title)}&reason=${encodeURIComponent(`${d.server.name} · ${hwKinds[r.kind]} · ${c.status.why} · /hardware?server=${d.server.id}&component=${r.id}`)}`}
            >
              Plan an upgrade
            </Link>
          </Button>
          <Button variant="outline" onClick={() => openForm("replace")}>
            Mark as replaced
          </Button>
        </div>
      )}
    </div>
  );
}
