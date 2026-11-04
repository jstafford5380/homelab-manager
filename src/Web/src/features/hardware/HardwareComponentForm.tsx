import { SelectField, SelectOption } from "@/components/SelectField";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  hardwareRequest,
  hwKinds,
  estimateUpsWatts,
  type HwComponent,
  type HwDetail,
  type HwSlot,
} from "./hardwareModel";

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
    [kind, setKind] = useState(
      c?.kind ?? (setupUpsKey ? "ups" : "power-supply"),
    ),
    [slots, setSlots] = useState<HwSlot[]>(
      replace ? [] : (component?.slots ?? []),
    );
  const [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const [deviceKey, setDeviceKey] = useState(
    replace ? "" : (setupUpsKey ?? c?.upsDeviceKey ?? ""),
  );
  const [factor, setFactor] = useState(
    replace ? "" : String(c?.upsPowerFactor ?? ""),
  );
  const [nominalVa, setNominalVa] = useState(
    replace ? "" : String(c?.upsNominalVa ?? ""),
  );
  const devices = detail.ups?.devices ?? [];
  const detected = deviceKey
    ? devices.find((device) => device.key === deviceKey)
    : undefined;
  const watts = estimateUpsWatts(
    detected,
    factor ? Number(factor) : null,
    nominalVa ? Number(nominalVa) : null,
  );
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
          upsDeviceKey:
            kind === "ups" && !replace && deviceKey ? deviceKey : null,
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
        <SelectField
          id="hw-kind"
          value={kind}
          disabled={!!setupUpsKey || (!!c && (!!c.detectedKey || replace))}
          onValueChange={(e) => setKind(e)}
        >
          {Object.entries(hwKinds).map(([key, label]) => (
            <SelectOption key={key} value={key}>
              {label}
            </SelectOption>
          ))}
        </SelectField>
      </div>
      {field(
        "name",
        "Name",
        c?.name ||
          c?.detectedName ||
          (setupUpsKey ? (detected?.model ?? detected?.name) : undefined),
        "text",
        true,
      )}
      {kind === "ups" && (
        <>
          {!replace && (
            <div className="field">
              <Label htmlFor="hw-ups-device">Detected UPS</Label>
              <SelectField
                id="hw-ups-device"
                value={deviceKey}
                onValueChange={(event) => setDeviceKey(event)}
              >
                <SelectOption value="">Unlinked UPS component</SelectOption>
                {deviceKey && !detected && (
                  <SelectOption value={deviceKey}>
                    Previously linked UPS · unavailable
                  </SelectOption>
                )}
                {devices
                  .filter(
                    (device) =>
                      device.key &&
                      !detail.components.some(
                        (other) =>
                          other.record.id !== c?.id &&
                          other.record.upsDeviceKey === device.key,
                      ),
                  )
                  .map((device) => (
                    <SelectOption key={device.key!} value={device.key!}>
                      {device.model ?? device.name} ({device.name})
                    </SelectOption>
                  ))}
              </SelectField>
            </div>
          )}
          {detected && !detected.hasSerial && deviceKey !== c?.upsDeviceKey && (
            <label key={deviceKey}>
              <input type="checkbox" name="confirmUpsWithoutSerial" required />{" "}
              Confirm this is my UPS. This device has no serial. Review the link
              if you replace it with the same model.
            </label>
          )}
          <div className="field">
            <Label htmlFor="hw-ups-factor">Power factor for estimates</Label>
            <Input
              id="hw-ups-factor"
              type="number"
              min="0"
              max="1"
              step="any"
              value={factor}
              onChange={(event) => setFactor(event.target.value)}
            />
            <small>
              Enter a value greater than zero and at most 1. Leave blank to omit
              the estimate.
            </small>
          </div>
          <div className="field">
            <Label htmlFor="hw-ups-va">Fallback nominal rating (VA)</Label>
            <Input
              id="hw-ups-va"
              type="number"
              min="0"
              max="1000000"
              step="any"
              value={nominalVa}
              onChange={(event) => setNominalVa(event.target.value)}
            />
            {detected?.nominalVa != null && (
              <small>
                The UPS reports {detected.nominalVa.toLocaleString()} VA. The
                reported rating takes priority.
              </small>
            )}
          </div>
          {watts != null && (
            <p>
              Preview · ≈{Math.round(watts).toLocaleString()} W estimated at the
              latest load.
            </p>
          )}
        </>
      )}
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
