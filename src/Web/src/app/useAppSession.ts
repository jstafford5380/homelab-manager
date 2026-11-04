import { validScanReports } from "../features/storage/scanModel";
import type { Snapshot } from "./overviewSnapshot";
import { useEffect, useRef, useState } from "react";
import { useCallback } from "react";
import { request } from "./api";
import { emptySnapshot } from "./overviewSnapshot";

export type Session = { authenticated: boolean; setupRequired: boolean };
export function useAppSession() {
  const [session, setSession] = useState<Session | null>(null);
  const [snapshot, setSnapshot] = useState<Snapshot>(emptySnapshot);
  const [refreshPhase, setRefreshPhase] = useState<
    "checking" | "current" | "unavailable"
  >("checking");
  const [refreshError, setRefreshError] = useState("");
  const [lastRefresh, setLastRefresh] = useState<number | null>(null);
  const refreshController = useRef<AbortController | null>(null);
  const refresh = useCallback(async () => {
    refreshController.current?.abort();
    const controller = new AbortController();
    refreshController.current = controller;
    let timedOut = false;
    const timeout = setTimeout(() => {
      timedOut = true;
      controller.abort();
    }, 15000);
    setRefreshPhase("checking");
    try {
      const state = await request(
        "/session",
        undefined,
        "GET",
        controller.signal,
      );
      if (
        !state ||
        typeof state.authenticated !== "boolean" ||
        typeof state.setupRequired !== "boolean"
      )
        throw new Error("Session data could not be read.");
      if (refreshController.current !== controller) return;
      setSession(state);
      if (state.authenticated) {
        const next = await request(
          "/overview",
          undefined,
          "GET",
          controller.signal,
        );
        if (
          !next ||
          !next.settings ||
          typeof next.settings.timezone !== "string" ||
          ![
            "recoveryAssessments",
            "capabilities",
            "connections",
            "disks",
            "findings",
            "plans",
            "recovery",
            "activity",
          ].every(
            (key) =>
              Array.isArray(next[key]) &&
              next[key].every(
                (row: unknown) => row !== null && typeof row === "object",
              ),
          ) ||
          !next.recoveryAssessments.every(
            (row: { assessment?: { protection?: unknown } }) =>
              typeof row.assessment?.protection === "string",
          )
        )
          throw new Error("Overview data could not be read.");
        new Intl.DateTimeFormat(undefined, {
          timeZone: next.settings.timezone,
        });
        if (refreshController.current !== controller) return;
        setSnapshot({
          ...next,
          scanReports: validScanReports(next.scanReports)
            ? next.scanReports
            : undefined,
        });
        setLastRefresh(Date.now());
      } else {
        setSnapshot(emptySnapshot);
        setLastRefresh(null);
      }
      setRefreshPhase("current");
      setRefreshError("");
    } catch (e) {
      if (refreshController.current !== controller) return;
      setRefreshPhase("unavailable");
      setRefreshError(
        timedOut
          ? "The refresh timed out. The control service did not respond within 15 seconds."
          : e instanceof Error
            ? e.message
            : "The overview could not be refreshed.",
      );
    } finally {
      clearTimeout(timeout);
    }
  }, []);
  useEffect(() => {
    void refresh();
    const timer = setInterval(() => void refresh(), 30000);
    return () => {
      clearInterval(timer);
      refreshController.current?.abort();
      refreshController.current = null;
    };
  }, [refresh]);
  return {
    session,
    snapshot,
    setSnapshot,
    refresh,
    refreshPhase,
    refreshError,
    lastRefresh,
  };
}
