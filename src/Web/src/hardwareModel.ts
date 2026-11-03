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
  upsDeviceKey?: string | null;
  upsPowerFactor?: number | null;
  upsNominalVa?: number | null;
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
type UpsReading = { value: number; observedUtc: string };
export type HwUpsDevice = {
  key: string | null;
  name: string;
  model: string | null;
  hasSerial: boolean;
  nominalVa: number | null;
  load: UpsReading | null;
  charge: UpsReading | null;
  runtime: UpsReading | null;
};
export function estimateUpsWatts(device: HwUpsDevice | undefined, factor: number | null | undefined, enteredVa: number | null | undefined) {
  const va = device?.nominalVa ?? enteredVa;
  const load = device?.load;
  if (va == null || !Number.isFinite(va) || va <= 0 || factor == null || !Number.isFinite(factor) || factor <= 0 || factor > 1 ||
      !load || Date.now() - Date.parse(load.observedUtc) > 15 * 60_000)
    return null;
  return va * (load.value / 100) * factor;
}
export type HwUps = {
  devices?: HwUpsDevice[] | null;
  status: "reporting" | "no-readings" | "denied" | "unsupported" | "unavailable";
  load?: UpsReading | null;
  charge?: UpsReading | null;
  runtime?: UpsReading | null;
  nominalVa?: number | null;
};
export type HwUpsMonthlyPower = {
  deviceKey: string;
  medianWatts: number;
  sampleCount: number;
  daysWithSamples: number;
  estimatedCostUsd: number | null;
};
export type HwDetail = {
  upsMonthlyPower?: HwUpsMonthlyPower[];
  electricityRateUsdPerKwh?: number | null;
  ups?: HwUps | null;
  lastCollectionUtc?: string | null;
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
  ups: "UPS",
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
