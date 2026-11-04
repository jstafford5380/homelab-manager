import {calendarDay,shortDate,usePlanningClock,type PlanningReason} from "./planning";
import {Link} from "react-router";
import {CircleAlert,CircleHelp,Clock,MapPin,Shield,TriangleAlert} from "lucide-react";
import {Tooltip,TooltipContent,TooltipProvider,TooltipTrigger} from "@/components/ui/tooltip";

export function ReasonChips({
  reasons,
  diskId,
  tone = "info",
  timezone,
}: {
  reasons: PlanningReason[];
  diskId?: string;
  tone?: string;
  timezone: string;
}) {
  const now = usePlanningClock();
  return (
    <TooltipProvider>
      <div className="planning-chips">
        {reasons.map((reason, i) => {
          let text = reason.summary || reason.detail,
            chipTone = tone,
            fix: { label: string; to: string } | undefined;
          let Icon = CircleAlert;
          if (reason.kind === "finding") {
            Icon = TriangleAlert;
            if (reason.findingId)
              fix = { label: "Evidence", to: `/?finding=${reason.findingId}` };
          }
          if (reason.kind === "warranty" && reason.endsUtc) {
            Icon = Shield;
            const days =
              calendarDay(reason.endsUtc, "UTC") - calendarDay(now, timezone);
            chipTone = days < 0 ? "neutral" : "warn";
            text =
              days < 0
                ? `Out of warranty since ${shortDate(reason.endsUtc, "UTC", true)}`
                : `Warranty ends ${shortDate(reason.endsUtc, "UTC")} · ${days} days`;
          }
          if (reason.kind === "age-review" && reason.purchasedUtc) {
            Icon = Clock;
            chipTone = "info";
            const today = new Date(calendarDay(now, timezone) * 86400000);
            const purchase = new Date(
              calendarDay(reason.purchasedUtc, "UTC") * 86400000,
            );
            let years = today.getUTCFullYear() - purchase.getUTCFullYear();
            if (
              today.getUTCMonth() < purchase.getUTCMonth() ||
              (today.getUTCMonth() === purchase.getUTCMonth() &&
                today.getUTCDate() < purchase.getUTCDate())
            )
              years--;
            text = `${years} years since purchase${reason.confidence === "estimated" ? " (estimated)" : ""}`;
          }
          if (reason.kind === "age-unknown") {
            Icon = CircleHelp;
            chipTone = "neutral";
            text = "Purchase date unknown";
            if (diskId)
              fix = { label: "Add date", to: `/storage?disk=${diskId}` };
          }
          if (reason.kind === "absent" || reason.kind === "ambiguous") {
            Icon = MapPin;
            chipTone = "warn";
            text =
              reason.kind === "absent"
                ? "Missing from last inventory"
                : "Identity ambiguous";
            if (diskId)
              fix = {
                label: reason.kind === "absent" ? "Locate" : "Resolve",
                to: `/storage?disk=${diskId}`,
              };
          }
          return (
            <span
              key={i}
              className={`planning-chip tone-${chipTone} ${reason.kind === "age-unknown" ? "dashed" : ""}`}
            >
              <Tooltip>
                <TooltipTrigger asChild>
                  <span tabIndex={0}>
                    <Icon size={13} />
                    {text}
                  </span>
                </TooltipTrigger>
                <TooltipContent className="max-w-xs">
                  {reason.detail}
                </TooltipContent>
              </Tooltip>
              {fix && <Link to={fix.to}>{fix.label}</Link>}
            </span>
          );
        })}
      </div>
    </TooltipProvider>
  );
}
