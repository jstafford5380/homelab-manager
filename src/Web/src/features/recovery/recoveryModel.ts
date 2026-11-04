export type Policy = {
  id: string;
  revision: string;
  asset: string;
  importance: string;
  owner: string;
  destination: string;
  destinationCapacity: string;
  restoreRequirements: string;
  scope: string;
  mechanism: string;
  independence: string;
  failureDomains: string;
  rpoHours: number;
  rtoHours: number;
  exclusions: string;
  schedule: string;
  retention: string;
  runbook: string;
  keyCustodyReference: string;
};
export type RecordRow = {
  policy: Policy;
  assessment: {
    protection: string;
    recoveryPoint: string;
    restore: string;
    rto: string;
    representedPointUtc: string | null;
    latestAttempt: string;
  };
  runs: {
    id: string;
    attemptedUtc: string;
    outcome: string;
    author: string;
    reviewByUtc: string;
    evidence: string;
  }[];
  exercises: {
    id: string;
    testedUtc: string;
    result: string;
    scope: string;
    checks: string;
    durationMinutes: number;
    author: string;
    reviewByUtc: string;
    evidence: string;
  }[];
};

export type RecoveryEditorDraft = {
  mode: "policy" | "runs" | "exercises";
  policy?: Policy;
};
