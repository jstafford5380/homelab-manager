import type { InvestmentTotal } from "./investment";
export type Status = { label: string; tone: string; why: string | null };
export type StorageDisk = {
  id: string;
  connectionId: string;
  nickname: string | null;
  model: string;
  serial: string;
  wwn: string | null;
  alias: string;
  mappingConfidence: string;
  sizeBytes: number | null;
  driveType: string | null;
  rotationRate: number | null;
  enclosure: number | null;
  slot: number | null;
  bay: string | null;
  purchasedUtc: string | null;
  warrantyEndUtc: string | null;
  notes: string;
  purchaseDateConfidence: string;
};
export type Acquisition = {
  revision: string;
  installedUtc: string | null;
  installationConfidence: string;
  purchaseCost: number | null;
  currency: string;
  costConfidence: string;
};
export type Drive = {
  apiPowerOnHours?: {
    status: string;
    hours: number | null;
    fetchedUtc: string | null;
    detail: string;
  } | null;
  temperatureNote?: string | null;
  group?: { id: string; name: string; role: string } | null;
  disk: StorageDisk;
  title: string;
  status: Status;
  poolGuid: string | null;
  poolName: string | null;
  memberState: string | null;
  readErrors: number | null;
  writeErrors: number | null;
  checksumErrors: number | null;
  bayConfirmed: boolean;
  temperatureC: number | null;
  temperatureFetchedUtc: string | null;
  acquisition: Acquisition | null;
  plan: {
    id: string;
    budget: number | null;
    currency: string;
    status: string;
  } | null;
  smart?: {
    passed: boolean | null;
    readUtc: string | null;
    skippedAsleep: boolean;
    error: string | null;
    powerOnHours: number | null;
    temperatureC: number | null;
    pendingSectors: number | null;
    reallocatedSectors: number | null;
    nvmeWearPercent: number | null;
    firmware: string | null;
    lastSelfTest: { entries: { result: string }[] } | null;
  } | null;
};
export type Pool = {
  guid: string;
  name: string;
  status: Status;
  layout: string;
  memberCount: number;
  toleratedFailures: number | null;
  usedBytes: number | null;
  sizeBytes: number | null;
  unmappedMembers: number;
  drives: Drive[];
  scan: {
    scanFunction: string | null;
    scanState: string | null;
    scanPercentage: number | null;
    scanStartedUtc: string | null;
    scanEndedUtc: string | null;
    scanErrors: number | null;
    scanRemainingSeconds?: number | null;
  } | null;
  capacityOutlook: {
    status: string;
    thresholdFraction: number;
    projectedDateAtThreshold: string | null;
  } | null;
  datasets: { name: string; usedBytes: number | null }[];
  datasetStatus: string;
};
export type Server = {
  id: string;
  name: string;
  host: string;
  freshness: string;
  lastSuccessUtc: string | null;
  poolCount: number;
  driveCount: number;
  rawCapacityBytes: number | null;
  attentionCount: number | null;
  alerts: {
    id: number;
    class: string;
    level: string;
    message: string;
    lastSeenUtc: string;
  }[];
  pools: Pool[];
  looseDrives: Drive[];
};
export type StorageData = {
  investment?: InvestmentTotal[];
  smartEnabled: boolean;
  servicePlanYears: number;
  servers: Server[];
};
export function validStorageData(value: unknown): value is StorageData {
  const object = (v: unknown): v is Record<string, unknown> =>
    v !== null && typeof v === "object" && !Array.isArray(v);
  const text = (v: unknown) => typeof v === "string";
  const optionalText = (v: unknown) => v === null || text(v);
  const number = (v: unknown) =>
    v === null || (typeof v === "number" && Number.isFinite(v));
  const time = (v: unknown) =>
    v === null || (typeof v === "string" && Number.isFinite(Date.parse(v)));
  const array = (v: unknown): v is unknown[] =>
    Array.isArray(v) && v.length <= 10000;
  const status = (v: unknown) =>
    object(v) &&
    text(v.label) &&
    ["ok", "warn", "bad", "neutral"].includes(String(v.tone)) &&
    optionalText(v.why);
  const drive = (v: unknown, serverId: unknown) => {
    if (
      !object(v) ||
      !object(v.disk) ||
      !text(v.title) ||
      !status(v.status) ||
      typeof v.bayConfirmed !== "boolean"
    )
      return false;
    const d = v.disk;
    if (v.temperatureNote !== undefined && !optionalText(v.temperatureNote))
      return false;
    if (
      v.group != null &&
      (!object(v.group) ||
        ![v.group.id, v.group.name, v.group.role].every(text))
    )
      return false;
    if (v.apiPowerOnHours != null) {
      const h = v.apiPowerOnHours;
      if (
        !object(h) ||
        ![
          "disabled",
          "supported",
          "unsupported",
          "denied",
          "not_reported",
          "unavailable",
          "stale",
        ].includes(String(h.status)) ||
        !number(h.hours) ||
        (typeof h.hours === "number" &&
          (!Number.isSafeInteger(h.hours) || h.hours < 0)) ||
        !time(h.fetchedUtc) ||
        !text(h.detail)
      )
        return false;
    }
    if (v.smart != null) {
      const s = v.smart;
      if (
        !object(s) ||
        !(s.passed === null || typeof s.passed === "boolean") ||
        !time(s.readUtc) ||
        typeof s.skippedAsleep !== "boolean" ||
        ![s.error, s.firmware].every(optionalText) ||
        ![
          s.powerOnHours,
          s.temperatureC,
          s.pendingSectors,
          s.reallocatedSectors,
          s.nvmeWearPercent,
        ].every(number) ||
        !(
          s.lastSelfTest === null ||
          (object(s.lastSelfTest) &&
            array(s.lastSelfTest.entries) &&
            s.lastSelfTest.entries.every((e) => object(e) && text(e.result)))
        )
      )
        return false;
    }
    if (
      !text(d.id) ||
      d.connectionId !== serverId ||
      ![
        d.model,
        d.serial,
        d.alias,
        d.mappingConfidence,
        d.notes,
        d.purchaseDateConfidence,
      ].every(text) ||
      ![d.nickname, d.wwn, d.driveType, d.bay].every(optionalText) ||
      ![d.sizeBytes, d.rotationRate, d.enclosure, d.slot].every(number) ||
      ![d.purchasedUtc, d.warrantyEndUtc].every(time)
    )
      return false;
    if (
      ![v.poolGuid, v.poolName, v.memberState].every(optionalText) ||
      ![v.readErrors, v.writeErrors, v.checksumErrors, v.temperatureC].every(
        number,
      ) ||
      !time(v.temperatureFetchedUtc)
    )
      return false;
    if (
      v.acquisition !== null &&
      (!object(v.acquisition) ||
        !text(v.acquisition.revision) ||
        !time(v.acquisition.installedUtc) ||
        !number(v.acquisition.purchaseCost) ||
        ![
          v.acquisition.installationConfidence,
          v.acquisition.currency,
          v.acquisition.costConfidence,
        ].every(text))
    )
      return false;
    return (
      v.plan === null ||
      (object(v.plan) &&
        [v.plan.id, v.plan.currency, v.plan.status].every(text) &&
        number(v.plan.budget))
    );
  };
  if (
    !object(value) ||
    typeof value.smartEnabled !== "boolean" ||
    typeof value.servicePlanYears !== "number" ||
    value.servicePlanYears < 1 ||
    !array(value.servers)
  )
    return false;
  return value.servers.every(
    (s) =>
      object(s) &&
      [s.id, s.name, s.host, s.freshness].every(text) &&
      time(s.lastSuccessUtc) &&
      [s.poolCount, s.driveCount, s.rawCapacityBytes, s.attentionCount].every(
        number,
      ) &&
      array(s.looseDrives) &&
      s.looseDrives.every((d) => drive(d, s.id)) &&
      array(s.alerts) &&
      s.alerts.every(
        (a) =>
          object(a) &&
          number(a.id) &&
          [a.class, a.level, a.message].every(text) &&
          time(a.lastSeenUtc),
      ) &&
      array(s.pools) &&
      s.pools.every(
        (p) =>
          object(p) &&
          [p.guid, p.name, p.layout, p.datasetStatus].every(text) &&
          status(p.status) &&
          [
            p.memberCount,
            p.toleratedFailures,
            p.usedBytes,
            p.sizeBytes,
            p.unmappedMembers,
          ].every(number) &&
          array(p.drives) &&
          p.drives.every((d) => drive(d, s.id)) &&
          array(p.datasets) &&
          p.datasets.every(
            (d) => object(d) && text(d.name) && number(d.usedBytes),
          ) &&
          (p.scan === null ||
            (object(p.scan) &&
              [p.scan.scanFunction, p.scan.scanState].every(optionalText) &&
              [p.scan.scanPercentage, p.scan.scanErrors].every(number) &&
              (p.scan.scanRemainingSeconds === undefined ||
                p.scan.scanRemainingSeconds === null ||
                (typeof p.scan.scanRemainingSeconds === "number" &&
                  Number.isSafeInteger(p.scan.scanRemainingSeconds) &&
                  p.scan.scanRemainingSeconds >= 0)) &&
              [p.scan.scanStartedUtc, p.scan.scanEndedUtc].every(time))) &&
          (p.capacityOutlook === null ||
            (object(p.capacityOutlook) &&
              text(p.capacityOutlook.status) &&
              number(p.capacityOutlook.thresholdFraction) &&
              time(p.capacityOutlook.projectedDateAtThreshold))),
      ),
  );
}
export const allDrives = (s: Server) => [
  ...s.pools.flatMap((p) => p.drives),
  ...s.looseDrives,
];
export const driveKind = (d: Drive) =>
  [
    d.disk.driveType || "Type unknown",
    d.disk.rotationRate ? d.disk.rotationRate + " rpm" : null,
  ]
    .filter(Boolean)
    .join(" · ");
export const relative = (v: string | null) => {
  if (!v) return "never";
  const m = Math.max(0, Math.floor((Date.now() - Date.parse(v)) / 60000));
  return m < 1
    ? "just now"
    : m < 60
      ? m + " minutes ago"
      : m < 1440
        ? Math.floor(m / 60) + " hours ago"
        : Math.floor(m / 1440) + " days ago";
};
export const age = (v: string | null | undefined) => {
  if (!v) return null;
  const m = Math.max(
    0,
    Math.floor((Date.now() - Date.parse(v)) / ((365.2425 / 12) * 86400000)),
  );
  return Math.floor(m / 12) + " y " + (m % 12) + " m";
};
export async function storageRequest(
  path: string,
  body?: unknown,
  method = "POST",
  signal?: AbortSignal,
) {
  const response = await fetch(
    "/api/v1" + path,
    body === undefined
      ? { signal }
      : {
          method,
          signal,
          headers: {
            "Content-Type": "application/json",
            "X-Homelab-Request": "1",
          },
          body: JSON.stringify(body),
        },
  );
  const result = await response.json().catch(() => null);
  if (!response.ok)
    throw new Error(
      result?.error ||
        "The control service didn't respond (" + response.status + ")",
    );
  return result;
}
