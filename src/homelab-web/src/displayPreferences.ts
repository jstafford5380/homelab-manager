import { createContext, useContext } from "react";
export type DisplayPreferences = {
  timezone: string;
  capacityUnits?: "binary" | "decimal";
};
export const DisplayPreferencesContext = createContext<DisplayPreferences>({
  timezone: "UTC",
  capacityUnits: "binary",
});
export function formatBytes(
  value: number | null | undefined,
  units: "binary" | "decimal" = "binary",
) {
  if (value === null || value === undefined || !Number.isFinite(value))
    return "Unknown";
  const base = units === "decimal" ? 1000 : 1024;
  const labels =
    units === "decimal"
      ? ["B", "kB", "MB", "GB", "TB", "PB", "EB"]
      : ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
  const index =
    value === 0
      ? 0
      : Math.min(
          labels.length - 1,
          Math.max(0, Math.floor(Math.log(Math.abs(value)) / Math.log(base))),
        );
  return `${(value / base ** index).toLocaleString(undefined, { maximumFractionDigits: 2 })} ${labels[index]}`;
}
export function useDisplayFormat() {
  const preferences = useContext(DisplayPreferencesContext);
  return {
    bytes: (value: number | null | undefined) =>
      formatBytes(value, preferences.capacityUnits),
    time: (value: string | null | undefined) => {
      if (!value || !Number.isFinite(Date.parse(value))) return "Unknown";
      return new Date(value).toLocaleString(undefined, {
        timeZone: preferences.timezone,
        timeZoneName: "short",
      });
    },
    timezone: preferences.timezone,
  };
}
