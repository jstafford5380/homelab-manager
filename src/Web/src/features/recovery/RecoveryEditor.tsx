import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Field } from "./RecoveryEditorFields";
import { Select } from "./RecoveryEditorFields";
import type { RecoveryEditorDraft } from "./recoveryModel";

export function RecoveryEditor({
  modal,
  busy,
  message,
  onClose,
  save,
}: {
  modal: RecoveryEditorDraft | null;
  busy: boolean;
  message: string;
  onClose: () => void;
  save: (fields: FormData) => Promise<void>;
}) {
  return (
    <Dialog
      open={!!modal}
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
    >
      <DialogContent
        style={{ maxHeight: "85vh", overflow: "auto", maxWidth: 680 }}
      >
        <DialogHeader>
          <DialogTitle>
            {modal?.mode === "policy"
              ? "Protection policy"
              : modal?.mode === "runs"
                ? "Record existing backup evidence"
                : "Record a completed restore exercise"}
          </DialogTitle>
        </DialogHeader>
        {modal && (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void save(new FormData(e.currentTarget));
            }}
          >
            {modal.mode !== "policy" && modal.policy && (
              <p className="muted" style={{ overflowWrap: "anywhere" }}>
                Recording evidence for {modal.policy.asset}:{" "}
                {modal.policy.scope} → {modal.policy.destination}. Confirm that
                your evidence covers this policy's current scope and
                destination.
              </p>
            )}
            {modal.mode === "policy" ? (
              <>
                <p className="muted">
                  Changing a policy creates a new revision. Earlier evidence
                  stays in history and must be revalidated for the revised
                  policy.
                </p>
                <div className="settings-grid">
                  <Field
                    name="asset"
                    label="Dataset or service"
                    value={modal.policy?.asset}
                  />
                  <Select
                    name="importance"
                    label="Recovery importance"
                    values={Array.from(
                      new Set([
                        "low",
                        "normal",
                        "important",
                        "critical",
                        ...(modal.policy?.importance
                          ? [modal.policy.importance]
                          : []),
                      ]),
                    )}
                    value={modal.policy?.importance || "important"}
                  />
                  <Field
                    name="destination"
                    label="Destination"
                    value={modal.policy?.destination}
                  />
                  <Field
                    name="scope"
                    label="Protected scope"
                    value={modal.policy?.scope}
                  />
                  <Select
                    name="mechanism"
                    label="Protection mechanism"
                    values={[
                      "independent backup",
                      "replication",
                      "local snapshot",
                      "offline/off-site copy",
                    ]}
                    value={modal.policy?.mechanism}
                  />
                  <Select
                    name="independence"
                    label="Destination independence"
                    values={["unverified", "shared", "verified"]}
                    value={modal.policy?.independence}
                  />
                  <Field
                    name="failureDomains"
                    label="Shared pool/power/building/credentials"
                    value={modal.policy?.failureDomains || "unknown"}
                  />
                  <Field
                    name="rpoHours"
                    label="RPO target (hours)"
                    type="number"
                    value={modal.policy?.rpoHours || 24}
                  />
                  <Field
                    name="rtoHours"
                    label="RTO target (hours)"
                    type="number"
                    value={modal.policy?.rtoHours || 4}
                  />
                  <Field
                    name="schedule"
                    label="Schedule"
                    required={false}
                    value={modal.policy?.schedule}
                  />
                  <Field
                    name="retention"
                    label="Retention"
                    required={false}
                    value={modal.policy?.retention}
                  />
                  <Field
                    name="exclusions"
                    label="Exclusions"
                    required={false}
                    value={modal.policy?.exclusions}
                  />
                  <Field
                    name="keyCustodyReference"
                    label="Key custody reference (no secrets)"
                    required={false}
                    value={modal.policy?.keyCustodyReference}
                  />
                </div>
                <div className="field">
                  <Label htmlFor="recovery-destinationCapacity">
                    Destination capacity notes (optional)
                  </Label>
                  <textarea
                    id="recovery-destinationCapacity"
                    name="destinationCapacity"
                    rows={2}
                    maxLength={4000}
                    aria-describedby="capacity-help"
                    defaultValue={modal.policy?.destinationCapacity}
                  />
                  <p id="capacity-help" className="muted">
                    Record total/free space, units, date checked and evidence
                    source. Leave blank if unknown; this is not a live capacity
                    check.
                  </p>
                </div>
                <div className="field">
                  <Label htmlFor="recovery-restoreRequirements">
                    Restore prerequisites (optional)
                  </Label>
                  <textarea
                    id="recovery-restoreRequirements"
                    name="restoreRequirements"
                    rows={3}
                    maxLength={10000}
                    aria-describedby="prerequisites-help"
                    defaultValue={modal.policy?.restoreRequirements}
                  />
                  <p id="prerequisites-help" className="muted">
                    Include configuration backups, permissions, application
                    state and dependencies. Use secure custody references for
                    secrets.
                  </p>
                </div>
                <div className="field">
                  <Label htmlFor="runbook">Recovery runbook</Label>
                  <textarea
                    id="runbook"
                    name="runbook"
                    rows={5}
                    defaultValue={modal.policy?.runbook}
                  />
                </div>
              </>
            ) : modal.mode === "runs" ? (
              <>
                <Select
                  name="outcome"
                  label="Outcome"
                  values={["success", "failed", "unknown"]}
                />
                <Field
                  name="attemptedUtc"
                  label="Attempt finished at (browser timezone)"
                  type="datetime-local"
                />
                <Field
                  name="recoveryPointUtc"
                  label="Recovery point represented at destination"
                  type="datetime-local"
                  required={false}
                />
                <label className="backup-checkbox">
                  <input name="destinationConfirmed" type="checkbox" />{" "}
                  Destination evidence confirms this recovery point
                </label>
                <Field
                  name="failureReason"
                  label="Failure reason"
                  required={false}
                />
                <Field name="evidence" label="Evidence / reference" />
                <Field
                  name="reviewByUtc"
                  label="Review this manual evidence by"
                  type="datetime-local"
                />
              </>
            ) : (
              <>
                <Field
                  name="testedUtc"
                  label="Test completed at (browser timezone)"
                  type="datetime-local"
                />
                <Field
                  name="recoveryPointUtc"
                  label="Recovery point restored"
                  type="datetime-local"
                />
                <Field
                  name="scope"
                  label="Scope actually restored"
                  value={modal.policy?.scope}
                />
                <Field
                  name="checks"
                  label="Integrity / application checks performed"
                />
                <Select
                  name="result"
                  label="Result"
                  values={["passed", "failed", "aborted"]}
                />
                <Field
                  name="durationMinutes"
                  label="Measured recovery duration (minutes)"
                  type="number"
                />
                <Field name="evidence" label="Evidence / reference" />
                <Field
                  name="reviewByUtc"
                  label="Review this restore evidence by"
                  type="datetime-local"
                />
              </>
            )}
            {message && (
              <p className="error" role="alert">
                {message}
              </p>
            )}
            <Button disabled={busy}>Save evidence</Button>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
