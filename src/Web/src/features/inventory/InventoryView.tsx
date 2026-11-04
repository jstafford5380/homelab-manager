import { types } from "./inventoryModel";
import { InventoryEditor } from "./InventoryEditor";
import { SelectField, SelectOption } from "@/components/SelectField";
import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Boxes, Plus, Download, Link, Pencil } from "lucide-react";
import type { Asset, Dependency } from "./inventoryModel";

export function InventoryView() {
  const [assets, setAssets] = useState<Asset[]>([]);
  const [dependencies, setDependencies] = useState<Dependency[]>([]);
  const [modal, setModal] = useState<{
    kind: "asset" | "dependency";
    asset?: Asset;
  } | null>(null);
  const [message, setMessage] = useState("");
  const [filter, setFilter] = useState("all");
  const [busy, setBusy] = useState(false);
  async function load() {
    const response = await fetch("/api/v1/inventory");
    if (!response.ok) throw new Error("Could not load inventory");
    const data = await response.json();
    setAssets(data.assets);
    setDependencies(data.dependencies);
  }
  useEffect(() => {
    void load().catch((e) => setMessage(String(e)));
  }, []);
  async function send(path: string, body: unknown, method = "POST") {
    setBusy(true);
    setMessage("");
    try {
      const response = await fetch("/api/v1/inventory/" + path, {
        method,
        headers: {
          "Content-Type": "application/json",
          "X-Homelab-Request": "1",
        },
        body: JSON.stringify(body),
      });
      if (!response.ok) {
        const error = await response.json();
        throw new Error(error.error || "Could not save inventory");
      }
      await load();
      setModal(null);
    } catch (e) {
      setMessage(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  const names = new Map(assets.map((a) => [a.id, a.name]));
  const active = assets.filter((a) => !a.archived);
  return (
    <>
      <div className="section-title">
        <h2>Inventory & shared dependencies</h2>
        <div style={{ display: "flex", gap: 10 }}>
          <Button variant="outline" asChild>
            <a href="/api/v1/inventory/export" download>
              <Download size={15} /> Export
            </a>
          </Button>
          <Button onClick={() => setModal({ kind: "asset" })}>
            <Plus size={15} /> Add asset
          </Button>
        </div>
      </div>
      <p className="muted" style={{ marginBottom: 20 }}>
        Manual inventory complements NAS discovery. Known shared controllers,
        power paths, and storage dependencies are investigation context—not
        proof of a cause.
      </p>
      <div className="section-title">
        <SelectField
          aria-label="Inventory filter"
          value={filter}
          onValueChange={(e) => setFilter(e)}
        >
          <SelectOption value="all">All active assets</SelectOption>
          {types.map((t) => (
            <SelectOption key={t}>{t}</SelectOption>
          ))}
          <SelectOption value="archived">Archived</SelectOption>
        </SelectField>
        <Button variant="outline" asChild>
          <a href="/api/v1/inventory/export?redacted=true" download>
            Export redacted inventory
          </a>
        </Button>
      </div>
      {message && !modal && (
        <p className="error" role="alert">
          {message}
        </p>
      )}
      <Card className="list-card">
        {assets
          .filter((a) =>
            filter === "archived"
              ? a.archived
              : !a.archived && (filter === "all" || a.type === filter),
          )
          .map((asset) => (
            <div className="inventory-entry" key={asset.id}>
              <div className="row">
                <Boxes />
                <div>
                  <h3>{asset.name}</h3>
                  <small>
                    {asset.type} · {asset.location || "Location unknown"} ·{" "}
                    {asset.owner || "Owner unassigned"}
                  </small>
                </div>
                <Badge variant="outline">{asset.verification}</Badge>
                {asset.type === "spare" && (
                  <Badge variant="secondary">{asset.quantity} on hand</Badge>
                )}
                <Button
                  variant="outline"
                  onClick={() => setModal({ kind: "asset", asset })}
                >
                  <Pencil size={14} /> Edit
                </Button>
              </div>
              <details style={{ padding: "0 0 16px 40px" }}>
                <summary>Purpose, runbook & dependencies</summary>
                <p>{asset.purpose}</p>
                <p className="muted">
                  Importance: {asset.criticality} · Version:{" "}
                  {asset.version || "Unknown"}
                </p>
                {asset.managementUrl && (
                  <a
                    href={asset.managementUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    Open management interface
                  </a>
                )}
                <p style={{ whiteSpace: "pre-wrap" }}>{asset.documentation}</p>
                <p style={{ whiteSpace: "pre-wrap" }}>{asset.runbook}</p>
                {asset.type === "spare" && (
                  <p>
                    Compatibility: {asset.compatibilityNotes || "Unverified"} ·
                    Estimated unit cost:{" "}
                    {asset.estimatedUnitCost === null
                      ? "Not set"
                      : `${asset.estimatedUnitCost} ${asset.currency}`}
                  </p>
                )}
                {dependencies
                  .filter(
                    (d) => d.sourceId === asset.id || d.targetId === asset.id,
                  )
                  .map((d) => (
                    <p key={d.id}>
                      {names.get(d.sourceId)} →{" "}
                      {d.relationship.replaceAll("_", " ")} →{" "}
                      {names.get(d.targetId)} ({d.verification})
                    </p>
                  ))}
              </details>
            </div>
          ))}
        {assets.length === 0 && (
          <div className="empty">
            <Boxes />
            <h3>Know what you own</h3>
            <p>
              Add hosts, services, spare drives, and shared hardware. No network
              scan is performed.
            </p>
          </div>
        )}
      </Card>
      <div className="section-title" style={{ marginTop: 32 }}>
        <h2>Dependency map</h2>
        <Button
          variant="outline"
          disabled={active.length < 2}
          onClick={() => setModal({ kind: "dependency" })}
        >
          <Link size={15} /> Add relationship
        </Button>
      </div>
      <Card className="list-card">
        {dependencies.length ? (
          dependencies.map((d) => (
            <div className="row" key={d.id}>
              <Link size={16} />
              <div>
                <h3>
                  {names.get(d.sourceId)} →{" "}
                  {d.relationship.replaceAll("_", " ")} →{" "}
                  {names.get(d.targetId)}
                </h3>
                <small>
                  {d.verification} · {d.evidence || "No verification evidence"}
                </small>
              </div>
              <Button
                variant="outline"
                disabled={busy}
                onClick={() => void send("dependencies/" + d.id, {}, "DELETE")}
              >
                Remove link
              </Button>
            </div>
          ))
        ) : (
          <p className="muted" style={{ padding: 18 }}>
            Add two assets to record their relationship and known shared failure
            domains.
          </p>
        )}
      </Card>
      <InventoryEditor
        modal={modal}
        active={active}
        busy={busy}
        message={message}
        onClose={() => setModal(null)}
        send={send}
      />
    </>
  );
}
