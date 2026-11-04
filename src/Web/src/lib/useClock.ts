import { useEffect, useState } from "react";

/** Update time-dependent labels without reading the wall clock during rendering. */
export function useClock(intervalMs = 10000) {
  const [now, setNow] = useState(Date.now);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}
