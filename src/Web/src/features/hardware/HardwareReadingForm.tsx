import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  gb,
  hardwareRequest,
  type HwDetail,
  type HwReading,
} from "./hardwareModel";

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
