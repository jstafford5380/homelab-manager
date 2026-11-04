import { useCallback, useEffect, useRef, useState } from "react";
import {
  storageRequest,
  validStorageData,
  type StorageData,
} from "./storageModel";

export function useStorageInventory(refreshRevision?: number | null) {
  const [data, setData] = useState<StorageData | null>(null),
    [error, setError] = useState(""),
    [loaded, setLoaded] = useState<string | null>(null);
  const request = useRef<AbortController | null>(null),
    mounted = useRef(false);
  const load = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    const timeout = setTimeout(() => controller.abort(), 15000);
    try {
      const next = await storageRequest(
        "/storage/overview",
        undefined,
        "GET",
        controller.signal,
      );
      if (!validStorageData(next))
        throw new Error("The storage response could not be read safely.");
      if (mounted.current && request.current === controller) {
        setData(next);
        setLoaded(new Date().toISOString());
        setError("");
      }
    } catch (e) {
      if (mounted.current && request.current === controller)
        setError(
          controller.signal.aborted
            ? "The request timed out."
            : e instanceof Error
              ? e.message
              : String(e),
        );
    } finally {
      clearTimeout(timeout);
    }
  }, []);
  useEffect(() => {
    mounted.current = true;
    void load();
    const timer = setInterval(() => void load(), 30000);
    return () => {
      mounted.current = false;
      clearInterval(timer);
      request.current?.abort();
    };
  }, [load]);
  const lastRevision = useRef(refreshRevision);
  useEffect(() => {
    if (lastRevision.current !== refreshRevision) {
      lastRevision.current = refreshRevision;
      void load();
    }
  }, [refreshRevision, load]);

  return { data, error, loaded, load };
}
