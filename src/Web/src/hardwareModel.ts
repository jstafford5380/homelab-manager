export type HwStatus = { label: string; tone: string; why: string };
export type HwPoint = {
  atUtc: string;
  value: number;
  source: string;
  readingId: string | null;
};
export type HwMetric = {
  source: string;
  average: number | null;
  latest: number | null;
  peak: number | null;
  p95: number | null;
  previousAverage: number | null;
  daysAboveLevel: number;
  readingsAboveLevel: number;
  readingCount: number;
  partial: boolean;
  reviewLevel: number | null;
  status: HwStatus | null;
  series: HwPoint[];
  cacheAverage: number | null;
  freeAverage: number | null;
};
export type HwSlot = { name: string; sizeBytes: number | null };
export type HwRecord = {
  id: string;
  serverId: string;
  revision: string;
  kind: string;
  detectedKey: string | null;
  detectedName: string | null;
  detectedSpec: string | null;
  detectedTotalBytes: number | null;
  name: string;
  model: string;
  spec: string;
  notes: string;
  pricePaid: number | null;
  purchasedUtc: string | null;
  installedUtc: string | null;
  warrantyEndUtc: string | null;
  datesEstimated: boolean;
  solderedBytes: number | null;
  maxSupportedBytes: number | null;
  retiredUtc: string | null;
};
export type HwComponent = {
  record: HwRecord;
  title: string;
  source: string;
  group: string;
  status: HwStatus;
  enteredTotalBytes: number | null;
  slots: HwSlot[];
  history: { atUtc: string; kind: string; text: string }[];
  use?: HwMetric | null;
  heat?: HwMetric | null;
};
export type HwReading = {
  id: string;
  revision: string;
  takenUtc: string;
  kind: string;
  periodMinutes: number | null;
  cpuPercent: number | null;
  memoryUsedBytes: number | null;
  temperatureC: number | null;
  powerWatts: number | null;
  note: string;
};
export type HwServer = {
  id: string;
  name: string;
  host: string;
  platform: string;
  version: string | null;
  freshness: string;
  lastSuccessUtc: string | null;
  uptimeSeconds: number | null;
  componentCount: number;
  watchCount: number | null;
  nearLimitCount: number | null;
};
export type HwDetail = {
  server: HwServer;
  coverage: {
    samples: number;
    expectedSamples: number;
    firstSampleUtc: string | null;
    collectionStartedUtc: string | null;
    days: number;
    coveredDays: number;
    gaps: { startUtc: string; endUtc: string }[];
    downsampled: boolean;
  };
  cpu: HwMetric;
  memory: HwMetric;
  temperature: HwMetric;
  power: HwMetric;
  totalBytes: number | null;
  components: HwComponent[];
  readings: HwReading[];
  cpuLevel: number;
  memoryLevel: number;
  temperatureLevel: number;
  sustainedMinutes: number;
  retiredComponents?: HwComponent[];
};
export const hwKinds: Record<string, string> = {
  processor: "Processor",
  memory: "Memory",
  motherboard: "Motherboard",
  "power-supply": "Power supply",
  cooling: "Cooling",
  "storage-controller": "Storage controller",
  "network-card": "Network card",
  "graphics-card": "Graphics card",
  case: "Case",
  "nas-unit": "NAS unit",
  other: "Other",
};
export const gb = (n: number | null | undefined) =>
  n == null
    ? "Unknown"
    : `${(n / 1073741824).toLocaleString(undefined, { maximumFractionDigits: 1 })} GiB`;
export const number = (n: number | null | undefined, unit = "") =>
  n == null
    ? "Unknown"
    : `${n.toLocaleString(undefined, { maximumFractionDigits: 1 })}${unit}`;
export async function hardwareRequest(
  path: string,
  body?: unknown,
  method = "POST",
  signal?: AbortSignal,
) {
  const response = await fetch("/api/v1/hardware" + path, {
    signal,
    method: body === undefined && method === "POST" ? "GET" : method,
    headers: {
      "X-Homelab-Request": "1",
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  if (!response.ok) {
    const error = await response.json().catch(() => ({}));
    throw new Error(
      error.error || `Hardware request failed (${response.status}).`,
    );
  }
  return response.status === 204 ? null : response.json();
}
