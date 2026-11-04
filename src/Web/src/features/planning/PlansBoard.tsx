import {useLayoutEffect,useRef,useState} from "react";
import {Link} from "react-router";
import {ArrowRight,Calendar,CheckCircle2,ClipboardList,Clock,HardDrive,Plus,Truck} from "lucide-react";
import {Button} from "@/components/ui/button";
import {Card} from "@/components/ui/card";
import {PlanEditor} from "./PlanEditor";
import type {Plan} from "./planning";
import {dueDays,focusPlan,money,shortDate} from "./planning";

export function PlansBoard({
  plans,
  disks,
  timezone,
  now,
  onChanged,
  onAdd,
}: {
  plans: Plan[];
  disks: { id: string; serial: string }[];
  timezone: string;
  now: number;
  onChanged: (saved?: Plan) => Promise<void>;
  onAdd: () => void;
}) {
  const [busy, setBusy] = useState<string | null>(null),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const pendingFocus = useRef<string | null>(null);
  useLayoutEffect(() => {
    if (pendingFocus.current) {
      focusPlan(pendingFocus.current);
      pendingFocus.current = null;
    }
  }, [plans]);
  async function advance(plan: Plan) {
    setBusy(plan.id);
    setError("");
    try {
      const status = plan.status === "planned" ? "ordered" : "complete";
      const response = await fetch(`/api/v1/plans/${plan.id}`, {
        method: "PUT",
        signal: AbortSignal.timeout(15000),
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify({ ...plan, status }),
      });
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        if (response.status === 409) await onChanged();
        throw new Error(body.error || "Could not move the plan.");
      }
      pendingFocus.current = plan.id;
      await onChanged(await response.json());
      setNotice(status === "ordered" ? "Moved to Ordered" : "Moved to Done");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Could not move the plan.");
    } finally {
      setBusy(null);
    }
  }
  return (
    <section id="planning-plans" aria-labelledby="planning-plans-title">
      <div className="section-title">
        <h2 id="planning-plans-title">Plans</h2>
        <Button variant="outline" size="sm" onClick={onAdd}>
          <Plus size={14} />
          Add plan
        </Button>
      </div>
      <p role="status" className="sr-only">
        {notice}
      </p>
      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}
      <div className="planning-board">
        {(
          [
            ["planned", "Planned", "Nothing planned", "info", ClipboardList],
            ["ordered", "Ordered", "Nothing on order", "progress", Truck],
            [
              "complete",
              "Done · last 30 days",
              "Nothing finished recently",
              "ok",
              CheckCircle2,
            ],
          ] as const
        ).map(([status, label, empty, tone, Icon]) => {
          const items = plans
            .filter(
              (p) =>
                p.status === status &&
                (status !== "complete" ||
                  (p.completedUtc &&
                    Date.parse(p.completedUtc) >= now - 30 * 86400000)),
            )
            .sort(
              (a, b) =>
                (a.dueUtc ? Date.parse(a.dueUtc) : Infinity) -
                  (b.dueUtc ? Date.parse(b.dueUtc) : Infinity) ||
                a.title.localeCompare(b.title),
            );
          return (
            <section
              key={status}
              aria-label={label}
              className={`planning-column tone-${tone}`}
            >
              <div className="planning-column-title">
                <Icon size={16} />
                <strong>{label}</strong>
                <span>{items.length}</span>
              </div>
              {!items.length && (
                <p className="planning-column-empty">{empty}</p>
              )}
              {items.map((p) => {
                const days = p.dueUtc ? dueDays(p.dueUtc, timezone, now) : null;
                const serial = disks.find((d) => d.id === p.diskId)?.serial;
                return (
                  <Card
                    key={p.id}
                    id={`plan-${p.id}`}
                    tabIndex={-1}
                    className="planning-plan"
                  >
                    <h3>{p.title}</h3>
                    {status === "complete" ? (
                      <div className="planning-done">
                        <span>
                          Done {shortDate(p.completedUtc!, timezone)}
                          {p.budget !== null
                            ? ` · ${money(p.budget, p.currency)}`
                            : ""}
                        </span>
                        <PlanEditor plan={p} onSaved={onChanged} />
                      </div>
                    ) : (
                      <>
                        <div className="planning-plan-meta">
                          {p.dueUtc && days !== null && (
                            <span
                              className={`planning-chip tone-${days < 0 ? "bad" : days <= 14 ? "warn" : "neutral"}`}
                            >
                              {days < 0 ? (
                                <Clock size={13} />
                              ) : (
                                <Calendar size={13} />
                              )}{" "}
                              {days < 0
                                ? `Overdue ${-days} day${days === -1 ? "" : "s"}`
                                : `Due ${shortDate(p.dueUtc, timezone)}${days <= 14 ? ` · ${days} day${days === 1 ? "" : "s"}` : ""}`}
                            </span>
                          )}
                          {serial && (
                            <Link
                              className="planning-chip tone-neutral"
                              to={`/storage?disk=${p.diskId}`}
                            >
                              <HardDrive size={13} />
                              {serial}
                            </Link>
                          )}
                          <span>{p.owner || "Unassigned"}</span>
                        </div>
                        {(p.notes ?? p.reason) && (
                          <p className="planning-plan-notes">
                            {p.notes ?? p.reason}
                          </p>
                        )}
                        <div className="planning-plan-actions">
                          {p.budget === null ? (
                            <PlanEditor
                              plan={p}
                              onSaved={onChanged}
                              addEstimate
                            />
                          ) : (
                            <strong>{money(p.budget, p.currency)}</strong>
                          )}
                          <PlanEditor plan={p} onSaved={onChanged} />
                          <Button
                            variant="outline"
                            size="sm"
                            disabled={busy !== null}
                            onClick={() => void advance(p)}
                          >
                            {busy === p.id
                              ? "Saving…"
                              : status === "planned"
                                ? "Mark ordered"
                                : "Mark done"}
                            <ArrowRight size={14} />
                          </Button>
                        </div>
                      </>
                    )}
                  </Card>
                );
              })}
            </section>
          );
        })}
      </div>
    </section>
  );
}
