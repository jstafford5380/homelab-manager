import { SelectField, SelectOption } from "@/components/SelectField";
import {
  lazy,
  Suspense,
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type ReactNode,
} from "react";
import { Link } from "react-router";
import { Dialog as DialogPrimitive } from "radix-ui";
import { ArrowLeft, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useDisplayFormat } from "./displayPreferences";
import { StorageBar, StoragePill, resilverEstimate } from "./StorageView";
import { StorageHealth } from "./StorageHealth";
import {
  age,
  allDrives,
  driveKind,
  storageRequest,
  type Drive,
  type Server,
  type StorageData,
} from "./storageModel";
const SmartReportImport = lazy(() =>
  import("./SmartReportImport").then((m) => ({ default: m.SmartReportImport })),
);
type Mode =
  | "profile"
  | "rename"
  | "location"
  | "note"
  | "plan"
  | "replace"
  | "import"
  | null;
function Field({
  name,
  label,
  value = "",
  type = "text",
  required = false,
}: {
  name: string;
  label: string;
  value?: string;
  type?: string;
  required?: boolean;
}) {
  return (
    <div className="field">
      <Label htmlFor={"storage-" + name}>{label}</Label>
      <Input
        id={"storage-" + name}
        name={name}
        type={type}
        defaultValue={value}
        required={required}
        step={type === "number" ? "0.01" : undefined}
      />
    </div>
  );
}
export function StorageDrivePanel({
  drive: d,
  server,
  data,
  mobile,
  editingInitially,
  close,
  restoreFocus,
  saved,
}: {
  drive: Drive;
  server: Server;
  data: StorageData;
  mobile: boolean;
  editingInitially: boolean;
  close: () => void;
  restoreFocus: () => void;
  saved: () => Promise<void>;
}) {
  const [mode, setMode] = useState<Mode>(editingInitially ? "profile" : null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const [history, setHistory] = useState<
      { atUtc: string; kind: string; text: string }[] | null
    >(null),
    [historyRevision, setHistoryRevision] = useState(0);
  const [historyError, setHistoryError] = useState(""),
    [newDisk, setNewDisk] = useState("");
  const heading = useRef<HTMLHeadingElement>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const feedbackRef = useRef<HTMLParagraphElement>(null);
  const initialMode = useRef(true);
  const noteRequest = useRef(crypto.randomUUID());
  useEffect(() => {
    if (error || notice) feedbackRef.current?.focus();
  }, [error, notice]);
  const { bytes, time } = useDisplayFormat();
  const month = (v: string) =>
    new Date(v).toLocaleDateString("en-US", {
      month: "short",
      year: "numeric",
      timeZone: "UTC",
    });
  useEffect(() => {
    if (initialMode.current) {
      initialMode.current = false;
      return;
    }
    if (mode)
      formRef.current
        ?.querySelector<HTMLElement>('input:not([type="hidden"]), [role="combobox"]')
        ?.focus();
  }, [mode]);
  const disk = d.disk,
    acquisition = d.acquisition;
  const years = acquisition?.installedUtc
    ? Math.max(
        0,
        (Date.now() - Date.parse(acquisition.installedUtc)) /
          (365.2425 * 86400000),
      )
    : null;
  const replacement = acquisition?.installedUtc
    ? new Date(acquisition.installedUtc)
    : null;
  if (replacement)
    replacement.setUTCFullYear(
      replacement.getUTCFullYear() + data.servicePlanYears,
    );
  const candidates = allDrives(server).filter(
    (x) => x.disk.id !== disk.id && x.disk.mappingConfidence !== "ambiguous",
  );
  const next = candidates.find((x) => x.disk.id === newDisk);
  const date = (v: string | null | undefined) => (v ? v.slice(0, 10) : "");
  const money = (v: number | null | undefined, currency = "USD") =>
    v == null
      ? "Not recorded"
      : new Intl.NumberFormat("en-US", {
          style: "currency",
          currency,
          maximumFractionDigits: 2,
        }).format(v);
  useEffect(() => {
    if (mobile) heading.current?.focus();
  }, [mobile]);
  useEffect(() => {
    const controller = new AbortController();
    void storageRequest(
      "/storage/drives/" + disk.id + "/history",
      undefined,
      "GET",
      controller.signal,
    )
      .then((rows) => {
        if (!controller.signal.aborted) {
          setHistory(rows);
          setHistoryError("");
        }
      })
      .catch((e) => {
        if (!controller.signal.aborted) setHistoryError(String(e));
      });
    return () => controller.abort();
  }, [disk.id, historyRevision]);
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    const f = new FormData(e.currentTarget);
    const text = (key: string) => String(f.get(key) ?? "");
    const utc = (key: string) =>
      text(key) ? text(key) + "T00:00:00.000Z" : null;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      if (mode === "rename") {
        await storageRequest(
          "/storage/drives/" + disk.id + "/nickname",
          { nickname: text("nickname") },
          "PUT",
        );
      } else if (mode === "profile") {
        await storageRequest(
          "/storage/drives/" + disk.id + "/profile",
          {
            revision: acquisition?.revision ?? null,
            nickname: disk.nickname,
            purchaseCost:
              text("purchaseCost") === "" ? null : Number(text("purchaseCost")),
            purchasedUtc: utc("purchasedUtc"),
            installedUtc: utc("installedUtc"),
            warrantyEndUtc: utc("warrantyEndUtc"),
            datesEstimated: f.get("datesEstimated") === "on",
            notes: text("notes"),
            currency: acquisition?.currency || "USD",
          },
          "PUT",
        );
      } else if (mode === "location") {
        await storageRequest(
          "/storage/drives/" + disk.id + "/location",
          {
            bay: text("bay"),
            confirmedSerial: text("confirmedSerial"),
            evidence: text("evidence"),
          },
          "PUT",
        );
      } else if (mode === "note") {
        await storageRequest("/storage/drives/" + disk.id + "/notes", {
          requestId: noteRequest.current,
          text: text("text"),
        });
        noteRequest.current = crypto.randomUUID();
      } else if (mode === "plan") {
        await storageRequest("/plans", {
          diskId: disk.id,
          title: text("title"),
          reason: text("reason"),
          budget: text("budget") === "" ? null : Number(text("budget")),
          currency: "USD",
        });
      } else if (mode === "replace") {
        await storageRequest("/replacements", {
          oldDiskId: disk.id,
          newDiskId: newDisk,
          occurredUtc: new Date().toISOString(),
          dateConfidence: "documented",
          poolGuid: d.poolGuid || "",
          membershipEvidence: text("membershipEvidence"),
          reason: text("reason"),
          evidence: text("evidence"),
          confirmedOldIdentity: text("confirmedOldIdentity").trim(),
          confirmedNewIdentity: text("confirmedNewIdentity").trim(),
        });
      }
      await saved();
      setHistoryRevision((n) => n + 1);
      setMode(null);
      setNotice("Saved");
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  const controls = (
    <div className="storage-form-actions">
      <Button
        variant="outline"
        type="button"
        disabled={busy}
        onClick={() => {
          setMode(null);
          setError("");
        }}
      >
        Cancel
      </Button>
      <Button type="submit" disabled={busy}>{busy ? "Saving…" : "Save"}</Button>
    </div>
  );
  const form = (children: ReactNode) => (
    <form
      ref={formRef}
      className="storage-box storage-form"
      onSubmit={(e) => void submit(e)}
    >
      {children}
      {error && (
        <p ref={feedbackRef} tabIndex={-1} role="alert" className="error">
          {error}
        </p>
      )}
      {controls}
    </form>
  );
  const driveHeading = (
    <h2 ref={heading} id="storage-drive-title" tabIndex={-1}>
      {d.title}
    </h2>
  );
  const title = (
    <>
      <StoragePill status={d.status} resilverEstimate={resilverEstimate(server.pools.find(p => p.guid === d.poolGuid), server.freshness !== "Reporting")} />
      <div className="storage-inline">
        {mobile ? (
          driveHeading
        ) : (
          <DialogPrimitive.Title asChild id="storage-drive-title">
            {driveHeading}
          </DialogPrimitive.Title>
        )}
        <button className="storage-link" onClick={() => setMode("rename")}>
          {disk.nickname ? "Rename" : "Give it a name"}
        </button>
      </div>
      <p className="storage-secondary">
        {bytes(disk.sizeBytes)} · {driveKind(d)} ·{" "}
        {d.poolName ? "in pool " + d.poolName : "not in a pool"}
      </p>
      <p className="storage-mono storage-secondary">
        Reported model: {disk.model || "Unknown"}
      </p>
    </>
  );
  const content = (
    <>
      <header className="storage-panel-head">
        {mobile ? (
          <Button variant="ghost" className="storage-back" onClick={close}>
            <ArrowLeft size={16} />
            {server.name} · {d.poolName || "Not in a pool"}
          </Button>
        ) : (
          <Button
            variant="outline"
            className="storage-panel-close"
            aria-label="Close drive details"
            onClick={close}
          >
            <X size={18} />
          </Button>
        )}
        {title}
      </header>
      <div className="storage-panel-body">
        {notice && (
          <p ref={feedbackRef} tabIndex={-1} role="status" className="storage-confirmed">
            {notice}
          </p>
        )}
        {mode === "rename" &&
          form(
            <Field
              name="nickname"
              label="Drive name"
              value={disk.nickname || ""}
            />,
          )}
        {d.status.label !== "Healthy" && (
          <p className={"storage-banner storage-tone-" + d.status.tone}>
            <span>
              <strong>Why “{d.status.label}”:</strong>{" "}
              {d.status.why || "Current drive health is unknown."}
            </span>
          </p>
        )}
        <StorageHealth
          drive={d}
          server={server}
          smartEnabled={data.smartEnabled}
        />
        <section aria-labelledby="storage-where">
          <h3 id="storage-where">Where it is</h3>
          <dl className="storage-kv">
            <dt>Location</dt>
            <dd>
              <strong>{disk.bay || "Not recorded"}</strong>
              {disk.enclosure != null && disk.slot != null && (
                <p>
                  Enclosure {disk.enclosure} · Slot {disk.slot}
                </p>
              )}
              <p className="storage-secondary">
                {d.bayConfirmed
                  ? "You confirmed this location."
                  : "Not yet confirmed. Check the physical label before pulling this drive."}
              </p>
              {!d.bayConfirmed && (
                <button
                  className="storage-link"
                  onClick={() => setMode("location")}
                >
                  I've checked it's here
                </button>
              )}
            </dd>
            <dt>Serial</dt>
            <dd className="storage-mono">{disk.serial || "Unknown"}</dd>
            <dt>Model</dt>
            <dd className="storage-mono">{disk.model || "Unknown"}</dd>
          </dl>
          {data.smartEnabled && d.smart?.firmware && (
            <p className="storage-secondary">Firmware: {d.smart.firmware}</p>
          )}
          {mode === "location" &&
            form(
              <>
                <Field
                  name="bay"
                  label="Bay label"
                  value={disk.bay || ""}
                  required
                />
                <Field
                  name="confirmedSerial"
                  label="Retype the physical serial"
                  required
                />
                <Field name="evidence" label="What you checked" required />
              </>,
            )}
        </section>
        <section aria-labelledby="storage-cost">
          <div className="storage-between">
            <h3 id="storage-cost">Cost &amp; lifespan</h3>
            {mode !== "profile" && (
              <button
                className="storage-link"
                onClick={() => setMode("profile")}
              >
                Edit
              </button>
            )}
          </div>
          {mode === "profile" ? (
            form(
              <>
                <div className="storage-form-grid">
                  <Field
                    name="purchaseCost"
                    label={
                      "Price paid (" + (acquisition?.currency || "USD") + ")"
                    }
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
                      Year{" "}
                      {Math.min(data.servicePlanYears, Math.floor(years) + 1)}{" "}
                      of {data.servicePlanYears}
                    </strong>
                    <span>
                      {years >= data.servicePlanYears
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
                    value={(years / data.servicePlanYears) * 100}
                    tone={
                      years >= data.servicePlanYears
                        ? "bad"
                        : years >= data.servicePlanYears - 1
                          ? "warn"
                          : "ok"
                    }
                    label={
                      Math.round((years / data.servicePlanYears) * 100) +
                      "% of planned service life used"
                    }
                  />
                  <small>
                    Based on your {data.servicePlanYears}-year replacement plan,
                    not a failure prediction.
                  </small>
                </div>
              )}
              <dl className="storage-kv">
                <dt>Paid</dt>
                <dd>
                  {money(
                    acquisition?.purchaseCost,
                    acquisition?.currency || "USD",
                  )}
                  {acquisition?.purchaseCost != null &&
                    years != null &&
                    " · about " +
                      money(
                        Math.round(
                          acquisition.purchaseCost / Math.max(1, years),
                        ),
                        acquisition.currency,
                      ) +
                      " per year so far"}
                </dd>
                <dt>Purchased</dt>
                <dd>
                  {disk.purchasedUtc
                    ? month(disk.purchasedUtc)
                    : "Not recorded"}
                  {disk.purchaseDateConfidence === "estimated" &&
                    " (estimated)"}
                </dd>
                <dt>Installed</dt>
                <dd>
                  {acquisition?.installedUtc ? (
                    month(acquisition.installedUtc) +
                    " · " +
                    age(acquisition.installedUtc) +
                    " ago"
                  ) : (
                    <button
                      className="storage-link"
                      onClick={() => setMode("profile")}
                    >
                      Add install date
                    </button>
                  )}
                  {acquisition?.installationConfidence === "estimated" &&
                    " (estimated)"}
                </dd>
                <dt>Warranty</dt>
                <dd>
                  {disk.warrantyEndUtc
                    ? (Date.parse(disk.warrantyEndUtc) < Date.now()
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
        <section aria-labelledby="storage-history">
          <div className="storage-between">
            <h3 id="storage-history">History</h3>
            <button className="storage-link" onClick={() => setMode("note")}>
              Add note
            </button>
          </div>
          {mode === "note" && form(<Field name="text" label="Note" required />)}
          {historyError ? (
            <p role="status">
              {historyError}
              <button
                className="storage-link"
                onClick={() => setHistoryRevision((n) => n + 1)}
              >
                Retry history
              </button>
            </p>
          ) : history ? (
            <ol className="storage-history">
              {history.map((h, i) => (
                <li key={i}>
                  <time dateTime={h.atUtc}>{time(h.atUtc)}</time>
                  <span>{h.text}</span>
                </li>
              ))}
            </ol>
          ) : (
            <p>Loading history…</p>
          )}
        </section>
        {mode === "plan" && (
          <section>
            <h3>Plan a replacement</h3>
            {form(
              <>
                <Field
                  name="title"
                  label="Title"
                  value={"Replace " + d.title}
                  required
                />
                <Field name="reason" label="Reason" required />
                <Field name="budget" label="Budget (USD)" type="number" />
              </>,
            )}
          </section>
        )}
        {mode === "replace" && (
          <section>
            <h3>Mark as replaced</h3>
            {form(
              <>
                <div className="field">
                  <Label htmlFor="storage-newDisk">New drive</Label>
                  <SelectField
                    id="storage-newDisk"
                    value={newDisk}
                    onValueChange={(e) => setNewDisk(e)}
                    required
                  >
                    <SelectOption value="">Choose a drive</SelectOption>
                    {candidates.map((x) => (
                      <SelectOption key={x.disk.id} value={x.disk.id}>
                        {x.title} · {x.disk.serial}
                      </SelectOption>
                    ))}
                  </SelectField>
                </div>
                <p className="storage-secondary">
                  The new drive's install date will be set to today. This
                  records your completed replacement locally.
                </p>
                <Field
                  name="confirmedOldIdentity"
                  label="Retype old drive serial (or WWN)"
                  required
                />
                <Field
                  name="confirmedNewIdentity"
                  label={
                    "Retype new drive serial (or WWN)" +
                    (next ? " · " + (next.disk.serial || next.disk.wwn) : "")
                  }
                  required
                />
                <Field
                  name="membershipEvidence"
                  label="Pool membership checked"
                  required
                />
                <Field name="reason" label="Reason" required />
                <Field name="evidence" label="What you checked" required />
              </>,
            )}
          </section>
        )}
        {data.smartEnabled && mode === "import" && (
          <Suspense fallback={<p>Loading report import…</p>}>
            <SmartReportImport
              disk={disk}
              initiallyOpen
              onClose={() => setMode(null)}
              onImported={async () => {
                await saved();
                setMode(null);
                setNotice("Saved");
              }}
            />
          </Suspense>
        )}
      </div>
      <footer className="storage-panel-actions">
        {!d.plan && (
          <Button variant="outline" onClick={() => setMode("plan")}>
            Plan a replacement
          </Button>
        )}
        <Button variant="outline" onClick={() => setMode("replace")}>
          Mark as replaced
        </Button>
        {data.smartEnabled && (
          <Button variant="outline" onClick={() => setMode("import")}>
            Import SMART report
          </Button>
        )}
      </footer>
    </>
  );
  return mobile ? (
    <section
      className="storage-panel storage-panel-mobile"
      aria-labelledby="storage-drive-title"
    >
      {content}
    </section>
  ) : (
    <DialogPrimitive.Root
      open
      onOpenChange={(open) => {
        if (!open) close();
      }}
    >
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="storage-scrim" />
        <DialogPrimitive.Content
          className="storage-panel"
          aria-labelledby="storage-drive-title"
          aria-describedby={undefined}
          onOpenAutoFocus={(e) => {
            e.preventDefault();
            heading.current?.focus();
          }}
          onCloseAutoFocus={(e) => {
            e.preventDefault();
            restoreFocus();
          }}
        >
          {content}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
