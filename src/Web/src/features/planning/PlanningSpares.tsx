import { Link } from "react-router";
import { Package } from "lucide-react";
import { money } from "./planning";
import type { Stock } from "./planning";
import { PlanningSkeleton as Skeleton } from "./PlanningSkeleton";

export function PlanningSpares({
  spares,
  error,
}: {
  spares: Stock[] | null;
  error: string;
}) {
  return (
    <section className="planning-spares" aria-label="Spares">
      <h2>
        <Package size={18} />
        Spares
      </h2>
      {!spares ? (
        error ? (
          <p>Unavailable with drive reviews.</p>
        ) : (
          <Skeleton rows={2} />
        )
      ) : (
        <>
          <div className="planning-stock-grid">
            {spares.length ? (
              spares.map((s) => (
                <article key={s.id} className="planning-spare">
                  <span
                    className={`planning-quantity tone-${s.quantity > 0 ? "ok" : "warn"}`}
                    aria-label={`${s.quantity} on hand`}
                  >
                    {s.quantity}
                  </span>
                  <div>
                    <h3>{s.name}</h3>
                    <p>
                      <span
                        className={
                          s.compatibleDriveCount && !error ? "text-ok" : ""
                        }
                      >
                        {error
                          ? "Checks out of date"
                          : s.compatibleDriveCount
                            ? `Checked for ${s.compatibleDriveCount} drive${s.compatibleDriveCount === 1 ? "" : "s"}`
                            : "Not checked against any drive"}
                      </span>
                      {s.estimatedUnitCost !== null &&
                        ` · ${money(s.estimatedUnitCost, s.currency)} each`}
                    </p>
                  </div>
                </article>
              ))
            ) : (
              <p>No spares recorded.</p>
            )}
          </div>
          <Link to="/inventory">Manage in Inventory →</Link>
        </>
      )}
    </section>
  );
}
