import { useEffect, useState } from "react";
import { hardwareRequest, type HwDetail, type HwServer } from "./hardwareModel";

type List = { hwmetrics: boolean; synology: boolean; servers: HwServer[] };
export function useHardwareData(
  selected: string | null,
  range: string,
  refreshRevision?: number | null,
) {
  const [list, setList] = useState<List | null>(null),
    [detail, setDetail] = useState<HwDetail | null>(null);
  const [error, setError] = useState(""),
    [retry, setRetry] = useState(0),
    [loaded, setLoaded] = useState<string | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort("timeout"), 15000);
    void hardwareRequest("/servers", undefined, "GET", controller.signal)
      .then((data) => {
        if (!controller.signal.aborted) {
          setList(data);
        }
      })
      .catch((e) => {
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setError(e.message);
      })
      .finally(() => clearTimeout(timeout));
    return () => {
      clearTimeout(timeout);
      controller.abort();
    };
  }, [retry, refreshRevision]);
  const serverId = selected ?? list?.servers[0]?.id;
  useEffect(() => {
    if (!serverId) return;
    const controller = new AbortController(),
      timeout = setTimeout(() => controller.abort("timeout"), 15000);
    void hardwareRequest(
      `/servers/${serverId}?range=${range}`,
      undefined,
      "GET",
      controller.signal,
    )
      .then((data) => {
        if (!controller.signal.aborted) {
          setError("");
          setDetail(data);
          setLoaded(new Date().toISOString());
        }
      })
      .catch((e) => {
        if (
          !controller.signal.aborted ||
          controller.signal.reason === "timeout"
        )
          setError(e.message);
      })
      .finally(() => clearTimeout(timeout));
    return () => {
      clearTimeout(timeout);
      controller.abort();
    };
  }, [serverId, range, retry, refreshRevision]);
  return {
    list,
    detail,
    error,
    loaded,
    serverId,
    setError,
    retry: () => setRetry((x) => x + 1),
  };
}
