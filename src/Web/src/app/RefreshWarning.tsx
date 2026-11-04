import { Button } from "@/components/ui/button";

export function RefreshWarning({
  refreshError,
  lastRefresh,
  timezone,
  refresh,
}: {
  refreshError: string;
  lastRefresh: number | null;
  timezone: string;
  refresh: () => Promise<void>;
}) {
  return (
    <section className="refresh-warning" role="alert">
      <div>
        <strong>Overview refresh unavailable</strong>
        <p>
          {lastRefresh
            ? `Showing last-known overview data loaded ${new Date(lastRefresh).toLocaleString(undefined, { timeZone: timezone })}.`
            : "No overview data has been loaded in this session."}{" "}
          This does not establish the current condition of your NAS.
        </p>
        <small>{refreshError}</small>
      </div>
      <Button variant="outline" onClick={() => void refresh()}>
        Retry overview
      </Button>
    </section>
  );
}
