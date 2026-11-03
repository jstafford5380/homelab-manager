import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
import {
  gb,
  hardwareRequest,
  hwKinds,
  estimateUpsWatts,
  type HwComponent,
  type HwDetail,
  type HwReading,
  type HwSlot,
} from "./hardwareModel";
const mobileSurfaces = new Set<HTMLElement>();
const previousInert = new Map<HTMLElement, boolean>();
function syncMobileSurfaces() {
  const top = [...mobileSurfaces].at(-1);
  if (!top) {
    for (const [element, inert] of previousInert) element.inert = inert;
    previousInert.clear();
    return;
  }
  for (const element of document.body.children) {
    if (!(element instanceof HTMLElement)) continue;
    if (!previousInert.has(element)) previousInert.set(element, element.inert);
    element.inert = element !== top;
  }
}
export function HardwareSurface({
  title,
  description,
  panel = false,
  close,
  children,
  returnFocus,
}: {
  title: string;
  description?: string;
  panel?: boolean;
  close: () => void;
  children: ReactNode;
  returnFocus?: HTMLElement | null;
}) {
  const [mobile, setMobile] = useState(
    () => matchMedia("(max-width: 700px)").matches,
  );
  const heading = useRef<HTMLHeadingElement>(null);
  const fullpage = useRef<HTMLElement>(null);
  useEffect(() => {
    const q = matchMedia("(max-width: 700px)");
    const change = () => setMobile(q.matches);
    q.addEventListener("change", change);
    return () => q.removeEventListener("change", change);
  }, []);
  useEffect(() => {
    if (mobile) heading.current?.focus();
  }, [mobile]);
  useEffect(() => {
    if (!mobile || !fullpage.current) return;
    const surface = fullpage.current;
    mobileSurfaces.add(surface);
    syncMobileSurfaces();
    return () => {
      mobileSurfaces.delete(surface);
      syncMobileSurfaces();
    };
  }, [mobile]);
  useEffect(
    () => () => {
      requestAnimationFrame(() => returnFocus?.focus());
    },
    [returnFocus],
  );
  if (mobile)
    return createPortal(
      <section
        ref={fullpage}
        className="hw-fullpage"
        aria-label={title}
        onKeyDown={(e) => {
          if (e.key === "Escape") close();
        }}
      >
        <Button variant="outline" onClick={close}>
          ← Back · Close
        </Button>
        <h2 ref={heading} tabIndex={-1}>
          {title}
        </h2>
        {description && <p>{description}</p>}
        {children}
      </section>,
      document.body,
    );
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) close();
      }}
    >
      <DialogContent
        style={panel ? { translate: "0 0", transform: "none" } : undefined}
        className={`hw-surface ${panel ? "hw-panel" : "hw-form-surface"}`}
        onOpenAutoFocus={(e) => {
          e.preventDefault();
          requestAnimationFrame(() =>
            document
              .querySelector<HTMLElement>('[data-slot="dialog-title"]')
              ?.focus(),
          );
        }}
        aria-describedby={description ? undefined : undefined}
        onCloseAutoFocus={(e) => {
          e.preventDefault();
          returnFocus?.focus();
        }}
      >
        <DialogTitle tabIndex={-1}>{title}</DialogTitle>
        {description && <DialogDescription>{description}</DialogDescription>}
        {children}
      </DialogContent>
    </Dialog>
  );
}
export function HardwareReadingForm({
  detail,
  reading,
  saved,
  cancel,
}: {
  detail: HwDetail;
  reading?: HwReading;
  saved: () => void;
  cancel: () => void;
}) {
  const [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [kind, setKind] = useState(reading?.kind ?? "moment");
  const [initialTime] = useState(
    () => reading?.takenUtc ?? new Date().toISOString(),
  );
  const field = (
    name: string,
    label: string,
    value: number | null | undefined,
    max?: number,
  ) => (
    <div className="field">
      <Label htmlFor={`hw-${name}`}>{label}</Label>
      <Input
        id={`hw-${name}`}
        name={name}
        type="number"
        step="any"
        min={name === "temperatureC" ? -50 : 0}
        max={max}
        defaultValue={value ?? ""}
      />
    </div>
  );
  return (
    <form
      onSubmit={async (e) => {
        e.preventDefault();
        const form = new FormData(e.currentTarget),
          n = (key: string) =>
            form.get(key) === "" ? null : Number(form.get(key));
        setBusy(true);
        setError("");
        try {
          await hardwareRequest(
            `/servers/${detail.server.id}/readings${reading ? "/" + reading.id : ""}`,
            {
              revision: reading?.revision,
              takenUtc: new Date(
                String(form.get("takenUtc")) + "Z",
              ).toISOString(),
              kind,
              periodMinutes: kind === "average" ? n("periodMinutes") : null,
              cpuPercent: n("cpuPercent"),
              memoryUsedBytes:
                n("memoryGB") == null ? null : n("memoryGB")! * 1073741824,
              temperatureC: n("temperatureC"),
              powerWatts: n("powerWatts"),
              note: form.get("note"),
            },
            reading ? "PUT" : "POST",
          );
          saved();
        } catch (err) {
          setError(String(err instanceof Error ? err.message : err));
        } finally {
          setBusy(false);
        }
      }}
    >
      <div className="field">
        <Label htmlFor="hw-taken">Taken (UTC)</Label>
        <Input
          id="hw-taken"
          name="takenUtc"
          type="datetime-local"
          required
          defaultValue={initialTime.slice(0, 16)}
        />
      </div>
      <fieldset>
        <legend>What it shows</legend>
        <label>
          <input
            type="radio"
            name="kind"
            checked={kind === "moment"}
            onChange={() => setKind("moment")}
          />{" "}
          A moment in time
        </label>
        <label>
          <input
            type="radio"
            name="kind"
            checked={kind === "average"}
            onChange={() => setKind("average")}
          />{" "}
          An average over a period
        </label>
      </fieldset>
      {kind === "average" && (
        <div className="field">
          <Label htmlFor="hw-duration">Period (minutes)</Label>
          <Input
            id="hw-duration"
            name="periodMinutes"
            type="number"
            min="1"
            max="525600"
            required
            defaultValue={reading?.periodMinutes ?? 60}
          />
        </div>
      )}
      <div className="settings-grid">
        {field("cpuPercent", "CPU use (%)", reading?.cpuPercent, 100)}
        {field(
          "memoryGB",
          `Memory used by apps (GiB${detail.totalBytes ? " of " + gb(detail.totalBytes) : ""})`,
          reading?.memoryUsedBytes == null
            ? null
            : reading.memoryUsedBytes / 1073741824,
          detail.totalBytes == null
            ? undefined
            : detail.totalBytes / 1073741824,
        )}
        {field("temperatureC", "Temperature (°C)", reading?.temperatureC, 200)}
        {field("powerWatts", "Power draw (W)", reading?.powerWatts, 1000000)}
      </div>
      <div className="field">
        <Label htmlFor="hw-reading-note">Note</Label>
        <textarea
          id="hw-reading-note"
          name="note"
          maxLength={2000}
          defaultValue={reading?.note ?? ""}
        />
      </div>
      <p>
        Readings show as dots on the charts, apart from collected samples, and
        are labelled “entered by you” wherever they’re used.
      </p>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <div className="hw-actions">
        <Button
          type="button"
          variant="outline"
          onClick={cancel}
          disabled={busy}
        >
          Cancel
        </Button>
        <Button disabled={busy}>{busy ? "Saving…" : "Save reading"}</Button>
      </div>
    </form>
  );
}
export function HardwareComponentForm({
  detail,
  component,
  replace = false,
  setupUpsKey,
  saved,
  cancel,
}: {
  detail: HwDetail;
  component?: HwComponent;
  replace?: boolean;
  setupUpsKey?: string;
  saved: (id?: string) => void;
  cancel: () => void;
}) {
  const c = component?.record,
    [kind, setKind] = useState(c?.kind ?? (setupUpsKey ? "ups" : "power-supply")),
    [slots, setSlots] = useState<HwSlot[]>(
      replace ? [] : (component?.slots ?? []),
    );
  const [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const [deviceKey, setDeviceKey] = useState(replace ? "" : setupUpsKey ?? c?.upsDeviceKey ?? "");
  const [factor, setFactor] = useState(replace ? "" : String(c?.upsPowerFactor ?? ""));
  const [nominalVa, setNominalVa] = useState(replace ? "" : String(c?.upsNominalVa ?? ""));
  const devices = detail.ups?.devices ?? [];
  const detected = deviceKey ? devices.find(device => device.key === deviceKey) : undefined;
  const watts = estimateUpsWatts(detected, factor ? Number(factor) : null, nominalVa ? Number(nominalVa) : null);
  const field = (
    name: string,
    label: string,
    value?: string | number | null,
    type = "text",
    required = false,
  ) => (
    <div className="field">
      <Label htmlFor={`hw-c-${name}`}>{label}</Label>
      <Input
        id={`hw-c-${name}`}
        name={name}
        type={type}
        required={required}
        defaultValue={replace ? "" : (value ?? "")}
        maxLength={name === "name" ? 200 : 2000}
        min={type === "number" ? 0 : undefined}
        step={type === "number" ? "any" : undefined}
      />
    </div>
  );
  return (
    <form
      onSubmit={async (e) => {
        e.preventDefault();
        const form = new FormData(e.currentTarget),
          numeric = (key: string) =>
            form.get(key) === "" || form.get(key) == null
              ? null
              : Number(form.get(key));
        const date = (key: string) =>
          form.get(key) ? String(form.get(key)) + "T00:00:00Z" : null;
        const input = {
          revision: c?.revision,
          kind,
          upsDeviceKey: kind === "ups" && !replace && deviceKey ? deviceKey : null,
          upsPowerFactor: kind === "ups" && factor ? Number(factor) : null,
          upsNominalVa: kind === "ups" && nominalVa ? Number(nominalVa) : null,
          confirmUpsWithoutSerial: form.get("confirmUpsWithoutSerial") === "on",
          name: form.get("name"),
          model: form.get("model"),
          spec: form.get("spec"),
          notes: form.get("notes"),
          pricePaid: numeric("pricePaid"),
          purchasedUtc: date("purchasedUtc"),
          installedUtc: date("installedUtc"),
          warrantyEndUtc: date("warrantyEndUtc"),
          datesEstimated: form.get("datesEstimated") === "on",
          slots: kind === "memory" ? slots : [],
          solderedBytes:
            kind === "memory" && numeric("solderedGB") != null
              ? numeric("solderedGB")! * 1073741824
              : null,
          maxSupportedBytes:
            kind === "memory" && numeric("maxGB") != null
              ? numeric("maxGB")! * 1073741824
              : null,
        };
        setBusy(true);
        setError("");
        try {
          const result = await hardwareRequest(
            `/servers/${detail.server.id}/components${c ? "/" + c.id : ""}${replace ? "/replace" : ""}`,
            replace ? { revision: c?.revision, successor: input } : input,
            c && !replace ? "PUT" : "POST",
          );
          saved(result?.id);
        } catch (err) {
          setError(err instanceof Error ? err.message : String(err));
        } finally {
          setBusy(false);
        }
      }}
    >
      {c?.detectedKey && !replace && (
        <p>
          Detected values stay unchanged. These fields are your annotations.
        </p>
      )}
      <div className="field">
        <Label htmlFor="hw-kind">Kind</Label>
        <select
          id="hw-kind"
          value={kind}
          disabled={!!setupUpsKey || !!c && (!!c.detectedKey || replace)}
          onChange={(e) => setKind(e.target.value)}
        >
          {Object.entries(hwKinds).map(([key, label]) => (
            <option key={key} value={key}>
              {label}
            </option>
          ))}
        </select>
      </div>
      {field("name", "Name", c?.name || c?.detectedName || (setupUpsKey ? detected?.model ?? detected?.name : undefined), "text", true)}
      {kind === "ups" && <>
        {!replace && <div className="field">
          <Label htmlFor="hw-ups-device">Detected UPS</Label>
          <select id="hw-ups-device" value={deviceKey} onChange={event => setDeviceKey(event.target.value)}>
            <option value="">Unlinked UPS component</option>
            {deviceKey && !detected && <option value={deviceKey}>Previously linked UPS · unavailable</option>}
            {devices.filter(device => device.key && !detail.components.some(other => other.record.id !== c?.id && other.record.upsDeviceKey === device.key))
              .map(device => <option key={device.key!} value={device.key!}>{device.model ?? device.name} ({device.name})</option>)}
          </select>
        </div>}
        {detected && !detected.hasSerial && deviceKey !== c?.upsDeviceKey &&
          <label key={deviceKey}><input type="checkbox" name="confirmUpsWithoutSerial" required /> Confirm this is my UPS. This device has no serial. Review the link if you replace it with the same model.</label>}
        <div className="field">
          <Label htmlFor="hw-ups-factor">Power factor for estimates</Label>
          <Input id="hw-ups-factor" type="number" min="0" max="1" step="any" value={factor} onChange={event => setFactor(event.target.value)} />
          <small>Enter a value greater than zero and at most 1. Leave blank to omit the estimate.</small>
        </div>
        <div className="field">
          <Label htmlFor="hw-ups-va">Fallback nominal rating (VA)</Label>
          <Input id="hw-ups-va" type="number" min="0" max="1000000" step="any" value={nominalVa} onChange={event => setNominalVa(event.target.value)} />
          {detected?.nominalVa != null && <small>The UPS reports {detected.nominalVa.toLocaleString()} VA. The reported rating takes priority.</small>}
        </div>
        {watts != null && <p>Preview · ≈{Math.round(watts).toLocaleString()} W estimated at the latest load.</p>}
      </>}
      {field("model", "Model or part number (you entered)", c?.model)}
      {field("spec", "Key specs (you entered)", c?.spec)}
      <div className="settings-grid">
        {field("pricePaid", "Price paid (USD)", c?.pricePaid, "number")}
        {field(
          "purchasedUtc",
          "Purchase date",
          c?.purchasedUtc?.slice(0, 10),
          "date",
        )}
        {field(
          "installedUtc",
          "Install date",
          c?.installedUtc?.slice(0, 10),
          "date",
        )}
        {field(
          "warrantyEndUtc",
          "Warranty ends",
          c?.warrantyEndUtc?.slice(0, 10),
          "date",
        )}
      </div>
      <label>
        <input
          type="checkbox"
          name="datesEstimated"
          defaultChecked={replace ? false : c?.datesEstimated}
        />{" "}
        These dates are estimates
      </label>
      {kind === "memory" && (
        <fieldset>
          <legend>Memory slots · You entered</legend>
          {slots.map((slot, i) => (
            <div className="hw-slot-fields" key={i}>
              <Label htmlFor={`hw-slot-name-${i}`}>Slot {i + 1}</Label>
              <Input
                id={`hw-slot-name-${i}`}
                value={slot.name}
                onChange={(e) =>
                  setSlots(
                    slots.map((s, j) =>
                      i === j ? { ...s, name: e.target.value } : s,
                    ),
                  )
                }
                required
                maxLength={100}
              />
              <Label htmlFor={`hw-slot-size-${i}`}>
                Size (GiB, blank if empty)
              </Label>
              <Input
                id={`hw-slot-size-${i}`}
                type="number"
                step="any"
                min="0.001"
                value={
                  slot.sizeBytes == null ? "" : slot.sizeBytes / 1073741824
                }
                onChange={(e) =>
                  setSlots(
                    slots.map((s, j) =>
                      i === j
                        ? {
                            ...s,
                            sizeBytes:
                              e.target.value === ""
                                ? null
                                : Number(e.target.value) * 1073741824,
                          }
                        : s,
                    ),
                  )
                }
              />
              <Button
                variant="outline"
                type="button"
                onClick={() => setSlots(slots.filter((_, j) => i !== j))}
              >
                Remove slot {i + 1}
              </Button>
            </div>
          ))}
          <Button
            type="button"
            variant="outline"
            disabled={slots.length >= 64}
            onClick={() =>
              setSlots([
                ...slots,
                { name: `DIMM ${slots.length + 1}`, sizeBytes: null },
              ])
            }
          >
            Add slot
          </Button>
          {field(
            "solderedGB",
            "Soldered memory (GiB)",
            c?.solderedBytes == null ? null : c.solderedBytes / 1073741824,
            "number",
          )}
          {field(
            "maxGB",
            "Board supports up to (GiB, you entered)",
            c?.maxSupportedBytes == null
              ? null
              : c.maxSupportedBytes / 1073741824,
            "number",
          )}
        </fieldset>
      )}
      <div className="field">
        <Label htmlFor="hw-component-notes">Notes</Label>
        <textarea
          id="hw-component-notes"
          name="notes"
          maxLength={10000}
          defaultValue={replace ? "" : (c?.notes ?? "")}
        />
      </div>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <div className="hw-actions">
        <Button
          type="button"
          variant="outline"
          onClick={cancel}
          disabled={busy}
        >
          Cancel
        </Button>
        <Button disabled={busy}>
          {busy
            ? "Saving…"
            : replace
              ? "Record replacement"
              : c
                ? "Save details"
                : "Add component"}
        </Button>
      </div>
    </form>
  );
}
