import { useEffect, useState } from "react";
import { priorities, priority } from "./planning";
import type { PlanningData } from "./planning";

export function usePlanningReview(revision: string) {
  const [data, setData] = useState<PlanningData | null>(null),
    [error, setError] = useState(""),
    [failedAt, setFailedAt] = useState<number | null>(null),
    [retry, setRetry] = useState(0);

  useEffect(() => {
    let disposed = false;
    const controller = new AbortController();
    let timer: ReturnType<typeof setTimeout>;
    async function load() {
      try {
        const response = await fetch("/api/v1/planning/review", {
          signal: AbortSignal.any([
            controller.signal,
            AbortSignal.timeout(15000),
          ]),
        });
        if (!response.ok)
          throw new Error(
            `Drive reviews couldn’t be loaded. The server returned ${response.status}.`,
          );
        const next: PlanningData = await response.json();
        if (
          !next ||
          !Array.isArray(next.reviews) ||
          !Array.isArray(next.spares) ||
          !Number.isFinite(Date.parse(next.generatedUtc)) ||
          next.reviews.some(
            (r) =>
              !r.diskId ||
              !Array.isArray(r.reasons) ||
              !r.spareFit ||
              !Object.hasOwn(priorities, priority(r)),
          )
        )
          throw new Error("Drive reviews returned an invalid response.");
        if (!disposed) {
          setData(next);
          setError("");
          setFailedAt(null);
        }
      } catch (e) {
        if (!disposed) {
          setError(
            e instanceof Error
              ? e.message
              : "Drive reviews couldn’t be loaded.",
          );
          setFailedAt(Date.now());
        }
      } finally {
        if (!disposed) timer = setTimeout(() => void load(), 30000);
      }
    }
    void load();
    return () => {
      disposed = true;
      controller.abort();
      clearTimeout(timer);
    };
  }, [revision, retry]);

  return { data, error, failedAt, retry: () => setRetry((x) => x + 1) };
}
