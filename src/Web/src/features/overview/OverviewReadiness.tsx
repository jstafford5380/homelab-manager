import { Link } from "react-router";
import { History, Package, ShoppingBag } from "lucide-react";
import { Card } from "@/components/ui/card";
import type { Snapshot } from "../../app/overviewSnapshot";
import { plural, money, type PlanningSummary } from "./overviewModel";

export function OverviewReadiness({
  plans,
  scanReports,
  planning,
}: {
  plans: Snapshot["plans"];
  scanReports: Snapshot["scanReports"];
  planning: PlanningSummary | "unavailable" | null;
}) {
  const openPlans = plans.filter((p) => p.status !== "complete");
  const scanCount = scanReports?.length ?? 0;
  const scanActive = scanCount > 0;
  const spares =
    planning && planning !== "unavailable"
      ? planning.spares.reduce((n, s) => n + s.quantity, 0)
      : null;
  return (
    <section aria-labelledby="ov-ready-title">
      <div className="section-title">
        <h2 id="ov-ready-title">Readiness</h2>
      </div>
      <Card className="ov-ready">
        <div>
          <Package size={18} aria-hidden="true" />
          <div>
            <strong>
              {spares === null
                ? "Spares unknown"
                : spares
                  ? plural(spares, "spare")
                  : "No spares recorded"}
            </strong>
            <small>
              {spares === null
                ? "Planning data isn't available right now."
                : spares === 0
                  ? "A compatible spare on the shelf shortens a rebuild."
                  : "Recorded in inventory"}
            </small>
          </div>
        </div>
        <div>
          <ShoppingBag size={18} aria-hidden="true" />
          <div>
            <strong>
              {planning && planning !== "unavailable" && planning.budgets.length
                ? planning.budgets
                    .map((b) => money(b.total, b.currency))
                    .join(" + ")
                : openPlans.length
                  ? "Budget not set"
                  : "No open plans"}
            </strong>
            <small>
              {planning &&
              planning !== "unavailable" &&
              planning.budgets.some((b) => b.withoutEstimate)
                ? `${plural(
                    planning.budgets.reduce((s, b) => s + b.withoutEstimate, 0),
                    "plan",
                  )} without an estimate`
                : `Across ${plural(openPlans.length, "open plan")}`}
            </small>
          </div>
        </div>
        <div>
          <History size={18} aria-hidden="true" />
          <div>
            <strong>
              {scanActive
                ? `${plural(scanCount, "scan report")} in progress`
                : scanReports
                  ? "No scrub or resilver in progress"
                  : "Scan summary unavailable"}
            </strong>
            <small>
              {scanActive ? (
                "Shown under Scrub & resilver reports."
              ) : (
                <>
                  {scanReports
                    ? "In the latest retained pool snapshots. This does not establish that all pools were checked. "
                    : "Review storage history and collection coverage. "}
                  <Link to="/storage">Review storage history</Link>
                </>
              )}
            </small>
          </div>
        </div>
      </Card>
    </section>
  );
}
