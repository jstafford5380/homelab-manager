import { Pencil } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { HardwarePill } from "./HardwareCharts";
import { hwKinds, type HwDetail } from "./hardwareModel";

export function HardwareInventory({
  d,
  stale,
  asOf,
  onOpen,
  onEdit,
}: {
  d: HwDetail;
  stale: boolean;
  asOf: number;
  onOpen: (id: string, opener: HTMLElement) => void;
  onEdit: (id: string) => void;
}) {
  return (
    <section aria-labelledby="hw-components">
      <h2 id="hw-components">Components</h2>
      <p className="hw-coverage">
        {d.components.length} components ·{" "}
        {d.components.filter((c) => c.source !== "You entered").length}{" "}
        detected, {d.components.filter((c) => c.source !== "Detected").length}{" "}
        entered by you
        {d.server.watchCount ? ` · ${d.server.watchCount} to watch` : ""}
        {d.server.nearLimitCount
          ? ` · ${d.server.nearLimitCount} near its limit`
          : ""}
      </p>
      <Card className="hw-inventory">
        {["Compute", "Power & cooling", "Expansion & chassis"].map((group) => {
          const items = d.components.filter((c) => c.group === group);
          return items.length ? (
            <section key={group}>
              <h3>{group}</h3>
              <table>
                <thead>
                  <tr>
                    {[
                      "Status",
                      "Component",
                      "Details",
                      "Source",
                      "In service",
                    ].map((h) => (
                      <th key={h}>{h}</th>
                    ))}
                    <th className="hw-component-actions">
                      <span className="sr-only">Actions</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((c) => (
                    <tr key={c.record.id}>
                      <td>
                        <HardwarePill status={c.status} stale={stale} />
                      </td>
                      <td>
                        <button
                          className="hw-component-name"
                          onClick={(e) => {
                            onOpen(c.record.id, e.currentTarget);
                          }}
                        >
                          {c.title}
                        </button>
                        <small>{hwKinds[c.record.kind]}</small>
                      </td>
                      <td>
                        {c.record.detectedSpec && (
                          <span>
                            {c.record.detectedSpec}{" "}
                            <small className="hw-source">Detected</small>
                          </span>
                        )}
                        {c.record.spec && (
                          <span>
                            {c.record.spec} <small>You entered</small>
                          </span>
                        )}
                        {!c.record.detectedSpec &&
                          !c.record.spec &&
                          "Not recorded"}
                      </td>
                      <td
                        className={
                          c.source !== "You entered" ? "hw-source" : ""
                        }
                      >
                        {c.source}
                      </td>
                      <td>
                        {c.record.installedUtc
                          ? `${Math.max(0, Math.floor((asOf - new Date(c.record.installedUtc).getTime()) / 86400000 / 30.44))} months${c.record.datesEstimated ? " · estimated" : ""}`
                          : "Not recorded"}
                      </td>
                      <td className="hw-component-actions">
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          aria-label={`Edit component ${c.title}`}
                          onClick={() => onEdit(c.record.id)}
                        >
                          <Pencil size={14} aria-hidden="true" />
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          ) : null;
        })}
        {!d.components.length && (
          <p className="hw-empty-components">
            No components yet. Detected parts appear after collection, or add a
            component.
          </p>
        )}
      </Card>
    </section>
  );
}
