import { type ReactNode } from "react";
import { Link } from "react-router";
import { StorageBar } from "./StorageIndicators";
import { age, type Drive } from "./storageModel";
import { StorageField as Field } from "./StorageField";
import { useClock } from "../../lib/useClock";

export function DriveProfileSection({
  d,
  servicePlanYears,
  editing,
  onEdit,
  renderForm,
}: {
  d: Drive;
  servicePlanYears: number;
  editing: boolean;
  onEdit: () => void;
  renderForm: (children: ReactNode) => ReactNode;
}) {
  const now = useClock();
  const disk = d.disk,
    acquisition = d.acquisition;
  const month = (v: string) =>
    new Date(v).toLocaleDateString("en-US", {
      month: "short",
      year: "numeric",
      timeZone: "UTC",
    });
  const years = acquisition?.installedUtc
    ? Math.max(
        0,
        (now - Date.parse(acquisition.installedUtc)) / (365.2425 * 86400000),
      )
    : null;
  const replacement = acquisition?.installedUtc
    ? new Date(acquisition.installedUtc)
    : null;
  if (replacement)
    replacement.setUTCFullYear(replacement.getUTCFullYear() + servicePlanYears);
  const date = (v: string | null | undefined) => (v ? v.slice(0, 10) : "");
  const money = (v: number | null | undefined, currency = "USD") =>
    v == null
      ? "Not recorded"
      : new Intl.NumberFormat("en-US", {
          style: "currency",
          currency,
          maximumFractionDigits: 2,
        }).format(v);

  return (
    <section aria-labelledby="storage-cost">
      <div className="storage-between">
        <h3 id="storage-cost">Cost &amp; lifespan</h3>
        {!editing && (
          <button className="storage-link" onClick={() => onEdit()}>
            Edit
          </button>
        )}
      </div>
      {editing ? (
        renderForm(
          <>
            <div className="storage-form-grid">
              <Field
                name="purchaseCost"
                label={"Price paid (" + (acquisition?.currency || "USD") + ")"}
                type="number"
                value={acquisition?.purchaseCost?.toString() ?? ""}
              />
              <Field
                name="purchasedUtc"
                label="Purchase date"
                type="date"
                value={date(disk.purchasedUtc)}
              />
              <Field
                name="installedUtc"
                label="Install date"
                type="date"
                value={date(acquisition?.installedUtc)}
              />
              <Field
                name="warrantyEndUtc"
                label="Warranty ends"
                type="date"
                value={date(disk.warrantyEndUtc)}
              />
            </div>
            <label className="storage-checkbox">
              <input
                type="checkbox"
                name="datesEstimated"
                defaultChecked={
                  disk.purchaseDateConfidence === "estimated" ||
                  acquisition?.installationConfidence === "estimated"
                }
              />
              These dates are estimates
            </label>
            <Field name="notes" label="Notes" value={disk.notes} />
          </>,
        )
      ) : (
        <>
          {years != null && replacement && (
            <div className="storage-box">
              <div className="storage-between">
                <strong>
                  Year {Math.min(servicePlanYears, Math.floor(years) + 1)} of{" "}
                  {servicePlanYears}
                </strong>
                <span>
                  {years >= servicePlanYears
                    ? "Replacement due"
                    : "Replace around " +
                      replacement.toLocaleDateString("en-US", {
                        month: "short",
                        year: "numeric",
                        timeZone: "UTC",
                      })}
                </span>
              </div>
              <StorageBar
                value={(years / servicePlanYears) * 100}
                tone={
                  years >= servicePlanYears
                    ? "bad"
                    : years >= servicePlanYears - 1
                      ? "warn"
                      : "ok"
                }
                label={
                  Math.round((years / servicePlanYears) * 100) +
                  "% of planned service life used"
                }
              />
              <small>
                Based on your {servicePlanYears}-year replacement plan, not a
                failure prediction.
              </small>
            </div>
          )}
          <dl className="storage-kv">
            <dt>Paid</dt>
            <dd>
              {money(acquisition?.purchaseCost, acquisition?.currency || "USD")}
              {acquisition?.purchaseCost != null &&
                years != null &&
                " · about " +
                  money(
                    Math.round(acquisition.purchaseCost / Math.max(1, years)),
                    acquisition.currency,
                  ) +
                  " per year so far"}
            </dd>
            <dt>Purchased</dt>
            <dd>
              {disk.purchasedUtc ? month(disk.purchasedUtc) : "Not recorded"}
              {disk.purchaseDateConfidence === "estimated" && " (estimated)"}
            </dd>
            <dt>Installed</dt>
            <dd>
              {acquisition?.installedUtc ? (
                month(acquisition.installedUtc) +
                " · " +
                age(acquisition.installedUtc) +
                " ago"
              ) : (
                <button className="storage-link" onClick={() => onEdit()}>
                  Add install date
                </button>
              )}
              {acquisition?.installationConfidence === "estimated" &&
                " (estimated)"}
            </dd>
            <dt>Warranty</dt>
            <dd>
              {disk.warrantyEndUtc
                ? (Date.parse(disk.warrantyEndUtc) < now
                    ? "Ended "
                    : "Until ") + month(disk.warrantyEndUtc)
                : "Not recorded"}
            </dd>
            <dt>Notes</dt>
            <dd>{disk.notes || "Not recorded"}</dd>
          </dl>
        </>
      )}
      {d.plan && (
        <Link className="storage-plan" to={"/planning?plan=" + d.plan.id}>
          <span>
            <strong>Replacement planned</strong> ·{" "}
            {d.plan.budget == null
              ? "No budget set"
              : money(d.plan.budget, d.plan.currency)}{" "}
            · {d.plan.status === "ordered" ? "ordered" : "not ordered yet"}
          </span>
          <strong>Open plan</strong>
        </Link>
      )}
    </section>
  );
}
