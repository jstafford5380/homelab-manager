import { HoverTip } from "@/components/HoverTip";
import {
  CircleCheck,
  TriangleAlert,
  CircleAlert,
  CircleHelp,
  type LucideIcon,
} from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { type Status } from "./storageModel";

const pillIcon: Record<string, LucideIcon> = {
  ok: CircleCheck,
  warn: TriangleAlert,
  bad: CircleAlert,
  neutral: CircleHelp,
};
export function StoragePill({
  status,
  resilverEstimate,
}: {
  status: Status;
  resilverEstimate?: string;
}) {
  const Icon = pillIcon[status.tone] ?? CircleHelp;
  return (
    <HoverTip
      content={status.label === "Resilvering" ? resilverEstimate : undefined}
    >
      <Badge
        tabIndex={
          status.label === "Resilvering" && resilverEstimate ? 0 : undefined
        }
        variant="outline"
        className={
          "storage-pill storage-tone-" +
          status.tone +
          (status.label === "Resilvering" ? " storage-pill-resilvering" : "")
        }
      >
        <Icon aria-hidden="true" />
        {status.label}
      </Badge>
    </HoverTip>
  );
}
export function StorageBar({
  value,
  label,
  tone,
}: {
  value: number | null;
  label: string;
  tone?: "ok" | "warn" | "bad";
}) {
  return (
    <div className="storage-bar" role="img" aria-label={label}>
      <span
        style={{
          width: Math.max(0, Math.min(100, value ?? 0)) + "%",
          backgroundColor: tone ? `var(--tone-${tone})` : undefined,
        }}
      />
    </div>
  );
}
