import { type ReactNode } from "react";
import { type Drive } from "./storageModel";
import { StorageField as Field } from "./StorageField";

export function DriveLocationSection({
  d,
  smartEnabled,
  editing,
  onConfirm,
  renderForm,
}: {
  d: Drive;
  smartEnabled: boolean;
  editing: boolean;
  onConfirm: () => void;
  renderForm: (children: ReactNode) => ReactNode;
}) {
  const disk = d.disk;
  return (
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
            <button className="storage-link" onClick={() => onConfirm()}>
              I've checked it's here
            </button>
          )}
        </dd>
        <dt>Serial</dt>
        <dd className="storage-mono">{disk.serial || "Unknown"}</dd>
        <dt>Model</dt>
        <dd className="storage-mono">{disk.model || "Unknown"}</dd>
      </dl>
      {smartEnabled && d.smart?.firmware && (
        <p className="storage-secondary">Firmware: {d.smart.firmware}</p>
      )}
      {editing &&
        renderForm(
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
  );
}
