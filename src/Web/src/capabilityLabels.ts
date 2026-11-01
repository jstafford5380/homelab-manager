const eventLabels: Record<string, string> = {
  "events:alert.list": "Passive alert updates",
  "events:pool.query": "Passive pool updates",
  "events:disk.query": "Passive disk updates",
};
export const capabilityLabel = (name: string) => eventLabels[name] || name.replaceAll("_", " ");
