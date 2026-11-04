import { SelectField, SelectOption } from "@/components/SelectField";
import { useEffect, useState } from "react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Boxes, Plus, Download, Link, Pencil } from "lucide-react";
type Asset = {
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
type Dependency = {
  id: string;
  sourceId: string;
  targetId: string;
  relationship: string;
  verification: string;
  evidence: string;
};
const types = [
  "host",
  "service",
  "enclosure",
  "controller",
  "backup target",
  "spare",
  "power",
  "network",
];
function Field({
  name,
  label,
  value = "",
  type = "text",
  required = false,
}: {
  name: string;
  label: string;
  value?: string | number;
  type?: string;
  required?: boolean;
}) {
  return (
    <div className="field">
      <Label htmlFor={"inventory-" + name}>{label}</Label>
      <Input
        id={"inventory-" + name}
        name={name}
        defaultValue={value}
        type={type}
        required={required}
      />
    </div>
  );
}
function Choice({
  name,
  label,
  values,
  value,
}: {
  name: string;
  label: string;
  values: string[];
  value?: string;
}) {
  return (
    <div className="field">
      <Label htmlFor={"inventory-" + name}>{label}</Label>
      <SelectField id={"inventory-" + name} name={name} defaultValue={value}>
        {values.map((v) => (
          <SelectOption key={v}>{v}</SelectOption>
        ))}
      </SelectField>
    </div>
  );
}
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
      <Dialog
        open={!!modal}
        onOpenChange={(open) => {
          if (!open) setModal(null);
        }}
      >
        <DialogContent
          style={{ maxHeight: "85vh", overflow: "auto", maxWidth: 680 }}
        >
          <DialogHeader>
            <DialogTitle>
              {modal?.kind === "asset"
                ? "Inventory asset"
                : "Dependency relationship"}
            </DialogTitle>
          </DialogHeader>
          {modal && (
            <form
              onSubmit={(e) => {
                e.preventDefault();
                const values = Object.fromEntries(
                  new FormData(e.currentTarget),
                );
                if (modal.kind === "dependency")
                  void send("dependencies", values);
                else
                  void send(
                    "assets" + (modal.asset ? "/" + modal.asset.id : ""),
                    {
                      ...values,
                      quantity: Number(values.quantity),
                      estimatedUnitCost: values.estimatedUnitCost
                        ? Number(values.estimatedUnitCost)
                        : null,
                      archived: values.archived === "on",
                    },
                    modal.asset ? "PUT" : "POST",
                  );
              }}
            >
              {modal.kind === "asset" ? (
                <>
                  <div className="settings-grid">
                    <Field
                      name="name"
                      label="Name"
                      value={modal.asset?.name}
                      required
                    />
                    <Choice
                      name="type"
                      label="Type"
                      values={types}
                      value={modal.asset?.type}
                    />
                    <Field
                      name="owner"
                      label="Owner"
                      value={modal.asset?.owner}
                    />
                    <Choice
                      name="criticality"
                      label="Importance"
                      values={["normal", "low", "important", "critical"]}
                      value={modal.asset?.criticality}
                    />
                    <Field
                      name="location"
                      label="Location"
                      value={modal.asset?.location}
                    />
                    <Field
                      name="version"
                      label="Version / model"
                      value={modal.asset?.version}
                    />
                    <Field
                      name="purpose"
                      label="Purpose"
                      value={modal.asset?.purpose}
                    />
                    <Field
                      name="managementUrl"
                      label="Management URL (no credentials)"
                      value={modal.asset?.managementUrl}
                    />
                    <Choice
                      name="verification"
                      label="Inventory verification"
                      values={["unverified", "verified"]}
                      value={modal.asset?.verification}
                    />
                    <Field
                      name="quantity"
                      label="Quantity / stock on hand"
                      type="number"
                      value={modal.asset?.quantity ?? 1}
                    />
                    <Field
                      name="estimatedUnitCost"
                      label="Estimated unit cost"
                      type="number"
                      value={modal.asset?.estimatedUnitCost ?? ""}
                    />
                    <Field
                      name="currency"
                      label="Currency"
                      value={modal.asset?.currency || "USD"}
                      required
                    />
                  </div>
                  <Field
                    name="compatibilityNotes"
                    label="Spare compatibility / assumptions"
                    value={modal.asset?.compatibilityNotes}
                  />
                  <Field
                    name="documentation"
                    label="Documentation references (no secrets)"
                    value={modal.asset?.documentation}
                  />
                  <div className="field">
                    <Label htmlFor="inventory-runbook">
                      Runbook / recovery instructions
                    </Label>
                    <textarea
                      id="inventory-runbook"
                      name="runbook"
                      rows={5}
                      defaultValue={modal.asset?.runbook}
                    />
                  </div>
                  <label className="backup-checkbox">
                    <input
                      name="archived"
                      type="checkbox"
                      defaultChecked={modal.asset?.archived}
                    />{" "}
                    Archive this asset (retain history)
                  </label>
                </>
              ) : (
                <>
                  <div className="settings-grid">
                    {["sourceId", "targetId"].map((name) => (
                      <div className="field" key={name}>
                        <Label htmlFor={"inventory-" + name}>
                          {name === "sourceId"
                            ? "Source asset"
                            : "Dependency / destination"}
                        </Label>
                        <SelectField id={"inventory-" + name} name={name}>
                          {active.map((a) => (
                            <SelectOption key={a.id} value={a.id}>
                              {a.name}
                            </SelectOption>
                          ))}
                        </SelectField>
                      </div>
                    ))}
                  </div>
                  <Choice
                    name="relationship"
                    label="Relationship"
                    values={[
                      "runs_on",
                      "stores_on",
                      "backed_up_to",
                      "powered_by",
                      "connected_to",
                    ]}
                  />
                  <Choice
                    name="verification"
                    label="Verification"
                    values={["unverified", "verified"]}
                  />
                  <Field name="evidence" label="Verification evidence" />
                </>
              )}
              {message && (
                <p className="error" role="alert">
                  {message}
                </p>
              )}
              <Button disabled={busy} style={{ marginTop: 16 }}>
                Save
              </Button>
            </form>
          )}
        </DialogContent>
      </Dialog>
    </>
  );
}
