import { formatInvestment } from "../../lib/investment";
import { Card } from "@/components/ui/card";
import { gb, number, type HwDetail } from "./hardwareModel";

export function HardwareServerFacts({
  d,
  stale,
  rangeLabel,
}: {
  d: HwDetail;
  stale: boolean;
  rangeLabel: string;
}) {
  return (
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
          ? ["Readings", String(d.readings.length), `In the last ${rangeLabel}`]
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
            : String(d.server.watchCount + (d.server.nearLimitCount ?? 0)),
          stale ? "Server out of date" : "Components",
        ],
        ["Total Investment", formatInvestment(d.investment), "Active hardware"],
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
  );
}
