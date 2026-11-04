export type Asset = {
  id: string;
  type: string;
  name: string;
  purpose: string;
  owner: string;
  criticality: string;
  location: string;
  version: string;
  managementUrl: string;
  documentation: string;
  runbook: string;
  verification: string;
  quantity: number;
  compatibilityNotes: string;
  estimatedUnitCost: number | null;
  currency: string;
  archived: boolean;
};
export type Dependency = {
  id: string;
  sourceId: string;
  targetId: string;
  relationship: string;
  verification: string;
  evidence: string;
};
export const types = [
  "host",
  "service",
  "enclosure",
  "controller",
  "backup target",
  "spare",
  "power",
  "network",
];

export type InventoryEditorDraft = {
  kind: "asset" | "dependency";
  asset?: Asset;
};
