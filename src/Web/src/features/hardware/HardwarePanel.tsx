import { useState } from "react";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import { HardwareChart, HardwarePill } from "./HardwareCharts";
import {
  gb,
  hwKinds,
  number,
  type HwComponent,
  type HwDetail,
} from "./hardwareModel";

export function HardwarePanel({
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
