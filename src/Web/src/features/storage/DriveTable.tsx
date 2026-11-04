import { Check, TriangleAlert, OctagonAlert } from "lucide-react";
import { useDisplayFormat } from "../../lib/displayPreferences";
import { age, driveKind, type Drive } from "./storageModel";
import { StoragePill } from "./StorageIndicators";

function serviceAgeColor(installedUtc: string | null | undefined) {
  if (!installedUtc || !Number.isFinite(Date.parse(installedUtc)))
    return undefined;
  const now = new Date();
  const sevenYearsAgo = new Date(now);
  sevenYearsAgo.setUTCFullYear(now.getUTCFullYear() - 7);
  const fiveYearsAgo = new Date(now);
  fiveYearsAgo.setUTCFullYear(now.getUTCFullYear() - 5);
  return Date.parse(installedUtc) < sevenYearsAgo.getTime()
    ? "var(--tone-bad)"
    : Date.parse(installedUtc) < fiveYearsAgo.getTime()
      ? "var(--tone-warn)"
      : undefined;
}
function ServiceAgeIcon({ installedUtc }: { installedUtc?: string | null }) {
  const color = serviceAgeColor(installedUtc);
  if (!color) return null;
  const older = color === "var(--tone-bad)";
  const Icon = older ? OctagonAlert : TriangleAlert;
  return (
    <Icon
      size={14}
      role="img"
      aria-label={`Past ${older ? 7 : 5} years in service`}
      style={{
        display: "inline-block",
        verticalAlign: "middle",
        marginLeft: 6,
        color,
      }}
    />
  );
}
export function DriveTable({
  drives,
  open,
  estimate,
}: {
  drives: Drive[];
  estimate?: string;
  open: (d: Drive, edit?: boolean) => void;
}) {
  const { bytes } = useDisplayFormat();
  const grouped = drives.some((d) => d.group != null);
  const buckets = new Map<
    string,
    { id: string; name: string | null; role: string; drives: Drive[] }
  >();
  for (const d of drives) {
    const id = d.group ? "known:" + d.group.id : "unknown";
    if (!buckets.has(id))
      buckets.set(id, {
        id,
        name: d.group?.name ?? (grouped ? "Group unknown" : null),
        role: d.group?.role ?? "unknown",
        drives: [],
      });
    buckets.get(id)!.drives.push(d);
  }
  const roles = [
    "data",
    "special",
    "dedup",
    "log",
    "cache",
    "spare",
    "unknown",
  ];
  const groups = Array.from(buckets.values()).sort((a, b) => {
    const rank = (role: string) =>
      roles.includes(role) ? roles.indexOf(role) : roles.length;
    return (
      rank(a.role) - rank(b.role) ||
      (a.name ?? "").localeCompare(b.name ?? "", undefined, { numeric: true })
    );
  });
  const groupHeading = (group: (typeof groups)[number]) => (
    <div className="storage-group-label">
      <strong>{group.name}</strong>
      <span>
        {group.role !== "unknown" && group.role + " · "}
        {group.drives.length} {group.drives.length === 1 ? "drive" : "drives"}
      </span>
    </div>
  );
  return (
    <>
      <div className="storage-table">
        <table>
          <thead>
            <tr>
              {[
                "Status",
                "Drive",
                "Capacity",
                "Serial",
                "Location",
                "In service",
              ].map((h) => (
                <th scope="col" key={h}>
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          {groups.map((group, index) => (
            <tbody
              key={group.id}
              className={
                grouped
                  ? "storage-drive-group storage-group-" + (index % 2)
                  : undefined
              }
            >
              {group.name && (
                <tr className="storage-group-heading">
                  <th scope="rowgroup" colSpan={6}>
                    {groupHeading(group)}
                  </th>
                </tr>
              )}
              {group.drives.map((d) => (
                <tr key={d.disk.id}>
                  <td>
                    <StoragePill
                      status={d.status}
                      resilverEstimate={estimate}
                    />
                  </td>
                  <td>
                    <button
                      className="storage-drive-link"
                      data-drive-id={d.disk.id}
                      onClick={() => open(d)}
                    >
                      {d.title}
                    </button>
                    <small>{driveKind(d)}</small>
                  </td>
                  <td>{bytes(d.disk.sizeBytes)}</td>
                  <td className="storage-mono">{d.disk.serial || "Unknown"}</td>
                  <td>
                    <strong>{d.disk.bay || "Not recorded"}</strong>
                    <small
                      className={
                        d.bayConfirmed
                          ? "storage-confirmed"
                          : "storage-unconfirmed"
                      }
                    >
                      {d.bayConfirmed && <Check size={12} />}{" "}
                      {d.bayConfirmed ? "Confirmed" : "Not confirmed"}
                    </small>
                    {d.disk.enclosure != null && d.disk.slot != null && (
                      <small>
                        Enclosure {d.disk.enclosure} · Slot {d.disk.slot}
                      </small>
                    )}
                  </td>
                  <td
                    style={{
                      color: serviceAgeColor(d.acquisition?.installedUtc),
                    }}
                  >
                    {age(d.acquisition?.installedUtc) || (
                      <button
                        className="storage-link"
                        onClick={() => open(d, true)}
                      >
                        Add install date
                      </button>
                    )}
                    <ServiceAgeIcon
                      installedUtc={d.acquisition?.installedUtc}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          ))}
        </table>
      </div>
      <div className="storage-drive-cards">
        {groups.map((group, index) => (
          <div
            key={group.id}
            className={
              grouped
                ? "storage-drive-group storage-group-" + (index % 2)
                : undefined
            }
          >
            {group.name && (
              <div className="storage-group-heading">{groupHeading(group)}</div>
            )}
            {group.drives.map((d) => (
              <button
                key={d.disk.id}
                className="storage-drive-card"
                data-drive-id={d.disk.id}
                onClick={() => open(d)}
              >
                <strong>{d.title}</strong>
                <StoragePill status={d.status} resilverEstimate={estimate} />
                <span>
                  {d.disk.bay || "Location unknown"} · {bytes(d.disk.sizeBytes)}{" "}
                  · {d.disk.serial || "Serial unknown"}
                </span>
                <span>
                  {d.bayConfirmed ? "Confirmed" : "Not confirmed"} ·{" "}
                  <span
                    style={{
                      color: serviceAgeColor(d.acquisition?.installedUtc),
                    }}
                  >
                    {age(d.acquisition?.installedUtc) || "Add install date"}
                    <ServiceAgeIcon
                      installedUtc={d.acquisition?.installedUtc}
                    />
                  </span>
                </span>
              </button>
            ))}
          </div>
        ))}
      </div>
    </>
  );
}
