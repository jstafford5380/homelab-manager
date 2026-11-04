import { useState } from "react";
import { RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";

export type ServerRefreshTarget = { serverId: string; target: "hardware" | "storage" };

export function RefreshServerButton({
  serverId,
  target,
  onCollected,
}: {
  serverId: string;
  target: "hardware" | "storage";
  onCollected: () => void | Promise<void>;
}) {
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");

  async function refreshNow() {
    setBusy(true);
    setMessage("Collecting from the NAS…");
    try {
      const response = await fetch(`/api/v1/connections/${encodeURIComponent(serverId)}/refresh`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify({ target }),
      });

      await onCollected();
      if (!response.ok) {
        const error = await response.json().catch(() => null);
        throw new Error(error?.error || `Refresh failed (${response.status}).`);
      }

      setMessage("NAS collection complete.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "NAS collection failed.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <Button variant="outline" disabled={busy} onClick={() => void refreshNow()}>
        <RefreshCw size={15} /> {busy ? "Refreshing…" : "Refresh"}
      </Button>
      <p aria-live="polite" className="muted">{message}</p>
    </div>
  );
}
