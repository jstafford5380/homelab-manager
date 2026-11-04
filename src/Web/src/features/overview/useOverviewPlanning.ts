import { useEffect, useState } from "react";
import { isPlanningSummary, type PlanningSummary } from "./overviewModel";

export function useOverviewPlanning(refreshRevision: number) {
  const [planning, setPlanning] = useState<
    PlanningSummary | "unavailable" | null
  >(null);
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/planning/review", {
      signal: AbortSignal.any([controller.signal, AbortSignal.timeout(15000)]),
    })
      .then((response) => (response.ok ? response.json() : null))
      .then(
        (data) =>
          !controller.signal.aborted &&
          setPlanning(isPlanningSummary(data) ? data : "unavailable"),
      )
      .catch(() => {
        if (!controller.signal.aborted) setPlanning("unavailable");
      });
    return () => controller.abort();
  }, [refreshRevision]);

  return planning;
}
