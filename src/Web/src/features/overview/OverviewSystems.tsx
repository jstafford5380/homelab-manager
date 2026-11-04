import { HoverTip } from "@/components/HoverTip";
import { Link } from "react-router";
import { Database } from "lucide-react";
import { Card } from "@/components/ui/card";
import { capabilityLabel } from "../../lib/capabilityLabels";
import type { Finding, Snapshot } from "../../app/overviewSnapshot";
import { OverviewTime as Time } from "./OverviewTime";
import { toneIcon, freshness, type Tone } from "./overviewModel";

const sourceLabels: Record<string, string> = {
  system_identity: "System identity",
  inventory: "Drive inventory",
  pools: "Pools",
  datasets: "Datasets",
  alerts: "NAS alerts",
  cached_temperatures: "Drive temperatures",
  smart: "SMART health",
  recovery: "Restore evidence",
};

const sourceStatuses: Record<string, [Tone, string]> = {
  supported: ["ok", "Reporting"],
  disabled: ["neutral", "Turned off"],
  unsupported: ["neutral", "Unsupported"],
  not_reported: ["neutral", "Not reported"],
  missing: ["neutral", "Not reported"],
  unavailable: ["warn", "Unavailable"],
  denied: ["warn", "Access denied"],
  stale: ["warn", "Out of date"],
};

const systemStatuses = {
  fresh: ["ok", "Reporting"],
  paused: ["neutral", "Paused"],
  never: ["warn", "No collection yet"],
  stale: ["warn", "Out of date"],
} as const;

function StatusPill({ tone, label }: { tone: Tone; label: string }) {
  const Icon = toneIcon[tone];
  return (
    <span className={`ov-pill tone-${tone}`}>
      <Icon size={12} aria-hidden="true" /> {label}
    </span>
  );
}

export function OverviewSystems({
  snapshot,
  issues,
  now,
}: {
  snapshot: Snapshot;
  issues: Finding[];
  now: number;
}) {
  const { connections, capabilities, disks } = snapshot;
  const systems = connections.map((connection) => {
    const sources = capabilities.filter(
      (source) => source.connectionId === connection.id,
    );
    const gaps = sources.filter((source) => source.status !== "supported");
    const [tone, label] = systemStatuses[freshness(connection, now)];
    const stats = [
      {
        label: "Last collected",
        value: connection.lastSuccessUtc ? (
          <Time value={connection.lastSuccessUtc} now={now} />
        ) : (
          "Not yet observed"
        ),
      },
      {
        label: "Drives",
        value: disks.filter(
          (disk) =>
            disk.connectionId === connection.id &&
            disk.present &&
            disk.lifecycle !== "retired",
        ).length,
      },
      {
        label: "Open findings",
        value: issues.filter((issue) => issue.connectionId === connection.id)
          .length,
      },
      { label: "Source gaps", value: gaps.length },
    ];
    return {
      connection,
      name: (
        <Link to={`/settings/connections/${connection.id}`}>
          {connection.name}
        </Link>
      ),
      status: <StatusPill tone={tone} label={label} />,
      stats,
      sources: [
        ...gaps,
        ...sources.filter((source) => source.status === "supported"),
      ],
    };
  });
  const [first] = systems;

  return (
    <section aria-labelledby="ov-systems-title">
      <div className="section-title">
        <h2 id="ov-systems-title">Systems</h2>
        <span className="muted">{systems.length} enrolled</span>
      </div>
      {systems.length === 1 ? (
        <Card className="ov-system">
          <div className="ov-system-head">
            <Database size={18} aria-hidden="true" />
            <h3>{first.name}</h3>
            {first.connection.version && (
              <small>{first.connection.version}</small>
            )}
            {first.status}
          </div>
          <p className="muted ov-system-status">{first.connection.status}</p>
          <dl>
            {first.stats.map(({ label, value }) => (
              <div key={label}>
                <dt>{label}</dt>
                <dd>{value}</dd>
              </div>
            ))}
          </dl>
          <div className="ov-sources">
            <div className="ov-sources-head">
              <h4>Sources</h4>
              <Link className="ov-section-link" to="/storage">
                Review coverage
              </Link>
            </div>
            {first.sources.length ? (
              <ul aria-label={`Sources for ${first.connection.name}`}>
                {first.sources.map(({ name, status }) => {
                  const [tone, label] = sourceStatuses[status] ?? [
                    "neutral",
                    status.replaceAll("_", " "),
                  ];
                  return (
                    <li key={name}>
                      <span>{sourceLabels[name] ?? capabilityLabel(name)}</span>
                      <StatusPill tone={tone} label={label} />
                    </li>
                  );
                })}
              </ul>
            ) : (
              <p className="muted">No sources reported yet.</p>
            )}
          </div>
        </Card>
      ) : (
        <Card className="ov-system-table" role="table" aria-label="Systems">
          <div className="ov-system-row ov-system-headers" role="row">
            {["System", "Status", ...first.stats.map(({ label }) => label)].map(
              (label) => (
                <span key={label} role="columnheader">
                  {label}
                </span>
              ),
            )}
          </div>
          {systems.map(({ connection, name, status, stats }) => (
            <div key={connection.id} className="ov-system-row" role="row">
              <div className="ov-system-name" role="cell">
                <Database size={16} aria-hidden="true" />
                <div>
                  <strong>{name}</strong>
                  <HoverTip content={connection.status}>
                    <small tabIndex={0}>{connection.status}</small>
                  </HoverTip>
                </div>
              </div>
              <div className="ov-system-row-status" role="cell">
                {status}
              </div>
              {stats.map(({ label, value }) => (
                <div key={label} role="cell">
                  <span className="ov-system-cell-label" aria-hidden="true">
                    {label}
                  </span>
                  {value}
                </div>
              ))}
            </div>
          ))}
        </Card>
      )}
    </section>
  );
}
