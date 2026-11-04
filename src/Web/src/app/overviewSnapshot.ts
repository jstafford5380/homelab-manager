import type { InvestmentTotal } from "../lib/investment";
import type { DisplayPreferences } from "../lib/displayPreferences";
import type { Plan } from "../features/planning/planning";
import type { ScanReport } from "../features/storage/scanModel";

export type Connection = {
  id: string;
  name: string;
  status: string;
  version?: string | null;
  lastSuccessUtc: string | null;
  pollSeconds: number;
  enabled?: boolean;
};
export type Disk = {
  id: string;
  connectionId: string;
  lastSeenUtc: string;
  serial: string;
  model: string;
  alias: string;
  wwn: string | null;
  mappingConfidence: string;
  bay: string | null;
  bayVerified: boolean;
  bayEvidence: string | null;
  present: boolean;
  purchasedUtc: string | null;
  warrantyEndUtc: string | null;
  notes: string;
  lifecycle: string;
  purchaseDateConfidence: string;
};
export type Finding = {
  id: string;
  connectionId?: string;
  diskId?: string | null;
  title: string;
  evidence: string;
  nextStep: string;
  severity: string;
  state: string;
  firstSeenUtc?: string;
  lastSeenUtc?: string;
  snoozedUntilUtc: string | null;
  reviewUtc: string | null;
};
export type Recovery = {
  id: string;
  asset: string;
  destination: string;
  mechanism: string;
  rpoHours: number;
  recoveryPointUtc: string | null;
  restoreResult: string;
};
export type Snapshot = {
  investment?: InvestmentTotal[];
  scanReports?: ScanReport[];
  recoveryAssessments: {
    policyId: string;
    assessment: { protection: string };
  }[];
  capabilities: { connectionId: string; name: string; status: string }[];
  settings: DisplayPreferences;
  connections: Connection[];
  disks: Disk[];
  findings: Finding[];
  plans: Plan[];
  recovery: Recovery[];
  activity: { id: number; action: string; detail: string; atUtc: string }[];
};

export const emptySnapshot: Snapshot = {
  recoveryAssessments: [],
  capabilities: [],
  settings: { timezone: "America/Phoenix", capacityUnits: "binary" },
  connections: [],
  disks: [],
  findings: [],
  plans: [],
  recovery: [],
  activity: [],
};
