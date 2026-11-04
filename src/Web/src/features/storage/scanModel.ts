export type ScanTimingFields = {
  scanStartedUtc?: string | null;
  scanEndedUtc?: string | null;
  scanPausedUtc?: string | null;
  scanRemainingSeconds?: number | null;
};
export function validScanTiming(row: ScanTimingFields) {
  return [row.scanStartedUtc, row.scanEndedUtc, row.scanPausedUtc].every(
    value => value == null || typeof value === "string" && Number.isFinite(Date.parse(value))) &&
    (row.scanRemainingSeconds == null || Number.isSafeInteger(row.scanRemainingSeconds) && row.scanRemainingSeconds >= 0);
}

export type ScanReport = ScanTimingFields & {
  connectionId: string; nasName: string; poolGuid: string; poolName: string; function: string; state: string;
  fetchedUtc: string; validUntilUtc: string | null; freshness: string; explanation: string; percentage: number | null; errors: number | null;
};
export function validScanReports(value: unknown): value is ScanReport[] {
  return Array.isArray(value) && value.every(row => row !== null && typeof row === "object" &&
    [row.connectionId,row.nasName,row.poolGuid,row.poolName,row.function,row.state,row.freshness,row.explanation].every(v => typeof v === "string") &&
    typeof row.fetchedUtc === "string" && Number.isFinite(Date.parse(row.fetchedUtc)) &&
    (row.validUntilUtc === null || typeof row.validUntilUtc === "string" && Number.isFinite(Date.parse(row.validUntilUtc))) &&
    (row.percentage === null || typeof row.percentage === "number" && Number.isFinite(row.percentage) && row.percentage >= 0 && row.percentage <= 100) &&
    validScanTiming(row) &&
    (row.errors === null || Number.isSafeInteger(row.errors) && row.errors >= 0));
}
