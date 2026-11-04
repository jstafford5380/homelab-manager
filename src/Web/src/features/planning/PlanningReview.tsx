import { useState } from "react";
import type { Plan } from "./planning";
import { PlansBoard } from "./PlansBoard";
import { CreatePlanDialog } from "./CreatePlanDialog";
import { usePlanningClock, type DriveReview } from "./planning";
import "./Planning.css";
import { usePlanningReview } from "./usePlanningReview";
import { PlanningOnboarding } from "./PlanningOnboarding";
import { PlanningSummary } from "./PlanningSummary";
import { DriveReviewQueue } from "./DriveReviewQueue";
import { PlanningSpares } from "./PlanningSpares";
import { PlanningFooter } from "./PlanningFooter";

export function PlanningReview({
  timezone,
  onChanged,
  plans,
  disks,
  createOpen,
  onCreateOpenChange,
  initialTitle,
  initialNotes,
}: {
  timezone: string;
  onChanged: (saved?: Plan) => Promise<void>;
  plans: Plan[];
  disks: { id: string; serial: string }[];
  createOpen: boolean;
  onCreateOpenChange: (value: boolean) => void;
  initialTitle?: string;
  initialNotes?: string;
}) {
  const [selected, setSelected] = useState<DriveReview | null>(null);
  const now = usePlanningClock();
  const { data, error, failedAt, retry } = usePlanningReview(
    JSON.stringify(plans),
  );
  const openPlans = plans.filter((p) => p.status !== "complete");
  const empty =
    !!data &&
    data.trackedDriveCount === 0 &&
    !plans.length &&
    !data.spares.length;
  const startPlan = (review: DriveReview | null) => {
    setSelected(review);
    onCreateOpenChange(true);
  };
  const selectedFit = selected?.spareFit;
  const fitPrice =
    selectedFit?.status === "compatible" &&
    selectedFit.validUntilUtc &&
    Date.parse(selectedFit.validUntilUtc) > now &&
    !error
      ? (data?.spares.find(
          (s) => s.id === selectedFit.assetId && s.quantity > 0,
        ) ?? null)
      : null;
  return (
    <div className="planning-page">
      {empty ? (
        <PlanningOnboarding onAdd={() => startPlan(null)} />
      ) : (
        <>
          <PlanningSummary
            data={data}
            error={error}
            plans={plans}
            timezone={timezone}
            now={now}
          />
          <div className="planning-review-layout">
            <DriveReviewQueue
              data={data}
              error={error}
              failedAt={failedAt}
              openPlans={openPlans}
              now={now}
              timezone={timezone}
              startPlan={startPlan}
              retry={retry}
            />
            <PlanningSpares spares={data?.spares ?? null} error={error} />
          </div>
          <PlansBoard
            plans={plans}
            disks={disks}
            timezone={timezone}
            now={now}
            onChanged={onChanged}
            onAdd={() => startPlan(null)}
          />
        </>
      )}
      <PlanningFooter data={data} timezone={timezone} />
      <CreatePlanDialog
        key={`${createOpen}-${selected?.diskId ?? "generic"}`}
        open={createOpen}
        onOpenChange={(value) => {
          onCreateOpenChange(value);
          if (!value) setSelected(null);
        }}
        review={selected}
        price={fitPrice}
        timezone={timezone}
        onChanged={onChanged}
        initialTitle={initialTitle}
        initialNotes={initialNotes}
      />
    </div>
  );
}
