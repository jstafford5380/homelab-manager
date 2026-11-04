import { CircleCheck, CircleHelp, Clock, Pause, TriangleAlert } from "lucide-react";

export type Connection = {
  id: string;
  configurationRevision: string;
  name: string;
  apiUri: string;
  connectHost: string | null;
  connectPort: number | null;
  pollSeconds: number;
  enabled: boolean;
  trustedCaPem: string | null;
  certificateSha256: string | null;
  status: string;
  lastSuccessUtc?: string | null;
  apiKeyReplacedUtc?: string | null;
  systemSerial: string | null;
  observedSystemSerial: string | null;
  systemIdentityState: string;
  systemIdentityCheckedUtc: string | null;
};
export function connectionStatus(connection: Connection) {
  if (connection.systemIdentityState === "changed" ||
      (connection.systemSerial && connection.observedSystemSerial && connection.systemSerial !== connection.observedSystemSerial))
    return { text: "Identity changed", tone: "bad", icon: TriangleAlert, attention: true };
  if (connection.systemIdentityState === "missing")
    return { text: "Identity unavailable", tone: "bad", icon: TriangleAlert, attention: true };
  if (/unsupported release/i.test(connection.status))
    return { text: "Unsupported release", tone: "neutral", icon: CircleHelp, attention: false };
  if (!connection.enabled)
    return { text: "Paused", tone: "neutral", icon: Pause, attention: false };
  if (/unavailable|denied|no source/i.test(connection.status))
    return { text: "Unavailable", tone: "bad", icon: TriangleAlert, attention: true };
  if (/awaiting/i.test(connection.status))
    return { text: "Awaiting validation", tone: "neutral", icon: CircleHelp, attention: false };
  if (!connection.lastSuccessUtc)
    return { text: "Never collected", tone: "neutral", icon: CircleHelp, attention: false };
  const age = Date.now() - Date.parse(connection.lastSuccessUtc);
  if (age < 0 || age > Math.max(300_000, connection.pollSeconds * 3_000))
    return { text: "Out of date", tone: "warn", icon: Clock, attention: false };
  return { text: "Reporting", tone: "ok", icon: CircleCheck, attention: false };
}
