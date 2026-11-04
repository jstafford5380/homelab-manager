import { SelectField, SelectOption } from "@/components/SelectField";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Field } from "./InventoryEditorFields";
import { Choice } from "./InventoryEditorFields";
import type { Asset, InventoryEditorDraft } from "./inventoryModel";
import { types } from "./inventoryModel";

export function InventoryEditor({
  modal,
  active,
  busy,
  message,
  onClose,
  send,
}: {
  modal: InventoryEditorDraft | null;
  active: Asset[];
  busy: boolean;
  message: string;
  onClose: () => void;
  send: (path: string, body: unknown, method?: string) => Promise<void>;
}) {
  return (
    <Dialog
      open={!!modal}
      onOpenChange={(open) => {
        if (!open) onClose();
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
              const values = Object.fromEntries(new FormData(e.currentTarget));
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
  );
}
