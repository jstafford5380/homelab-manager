import { Bell, ChevronRight } from "lucide-react";

export function AppHeader({
  page,
  refreshPhase,
}: {
  page: string;
  refreshPhase: "checking" | "current" | "unavailable";
}) {
  return (
    <header>
      <span>
        Workspace <ChevronRight size={14} /> <strong>{page}</strong>
      </span>
      <span
        className="header-right"
        role="status"
        aria-label="Overview refresh status"
        aria-live="off"
      >
        <span className={`status-dot status-${refreshPhase}`} />{" "}
        {refreshPhase === "current"
          ? "Overview refreshed"
          : refreshPhase === "checking"
            ? "Refreshing overview"
            : "Overview unavailable"}{" "}
        <Bell size={18} />
      </span>
    </header>
  );
}
