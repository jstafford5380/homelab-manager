import {useState} from "react";
import {Download} from "lucide-react";
import {Button} from "@/components/ui/button";

export function StorageExportButton() {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  async function download() {
    setBusy(true);
    setError("");
    try {
      const response = await fetch("/api/v1/storage/export.csv");
      if (!response.ok || !response.headers.get("content-type")?.includes("text/csv"))
        throw new Error(response.status === 401 ? "Sign in again to export your inventory." : "Storage couldn't be exported. Please try again.");
      const file = await response.blob();
      const url = URL.createObjectURL(file);
      const link = document.createElement("a");
      link.href = url;
      link.download = "homelab-storage-" + new Date().toISOString().replace(/[:.]/g, "-") + ".csv";
      document.body.appendChild(link);
      link.click();
      link.remove();
      // Allow the browser to start reading the object URL before releasing it.
      setTimeout(() => URL.revokeObjectURL(url), 10000);
    } catch (e) {
      setError(e instanceof Error && e.message.startsWith("Sign in") ? e.message : "Storage couldn't be exported. Please try again.");
    } finally {
      setBusy(false);
    }
  }
  return <div className="storage-export-control">
    <Button variant="outline" disabled={busy} onClick={() => void download()}
      aria-label={busy ? "Exporting… all storage inventory" : "Export CSV — all storage inventory"}>
      <Download size={15} aria-hidden="true" /> {busy ? "Exporting…" : "Export CSV"}
    </Button>
    {error && <p className="storage-export-error" role="alert">{error}</p>}
  </div>;
}
