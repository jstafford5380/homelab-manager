import {useEffect,useRef,useState} from "react";
import {Link} from "react-router";
import {Card} from "@/components/ui/card";
import {Badge} from "@/components/ui/badge";
import {Button} from "@/components/ui/button";
import {Label} from "@/components/ui/label";
import {Textarea} from "@/components/ui/textarea";
import {ArrowLeft,Check,HardDrive} from "lucide-react";

type Finding = {
  id: string;
  resolutionToken?: string;
  diskId: string | null;
  title: string;
  evidence: string;
  nextStep: string;
  rule: string;
  severity: string;
  state: string;
  firstSeenUtc: string;
  lastSeenUtc: string;
  evidenceObservedUtc: string | null;
  evidenceSource: string | null;
  evidenceUnit: string | null;
  evidenceSchema: string | null;
  evidenceEpoch: string | null;
  resolvedUtc: string | null;
  resolutionEvidence: string | null;
  assignedOwner: string | null;
  reviewUtc: string | null;
  snoozedUntilUtc: string | null;
};
export function FindingDetail({
  id,
  timezone,
  onChanged,
}: {
  id: string;
  timezone: string;
  onChanged: () => Promise<void>;
}) {
  const [resolution, setResolution] = useState<{ id: string; token: string } | null>(null);
  const [resolutionMessage, setResolutionMessage] = useState("");
  const messageRef = useRef<HTMLParagraphElement>(null);
  useEffect(() => { if (resolutionMessage) messageRef.current?.focus(); }, [resolutionMessage]);
  const [finding, setFinding] = useState<Finding | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const response = await fetch(
          `/api/v1/findings/${encodeURIComponent(id)}`,
          { signal: controller.signal },
        );
        if (!response.ok)
          throw new Error(
            response.status === 404
              ? "This finding could not be found. It may belong to a different installation or backup."
              : "Finding evidence could not be loaded. Check your connection and try again.",
          );
        const data: Finding = await response.json();
        if (!controller.signal.aborted) {
          setFinding(data);
          setError("");
        }
      } catch (e) {
        if (!controller.signal.aborted)
          setError(e instanceof Error ? e.message : String(e));
      }
    }
    void load();
    const timer = setInterval(() => void load(), 30000);
    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, [id, revision]);
  const current = finding?.id === id ? finding : null;
  const timestamp = (value: string | null) =>
    value
      ? new Date(value).toLocaleString(undefined, { timeZone: timezone })
      : "Unknown";
  async function acknowledge() {
    setBusy(true);
    setError("");
    try {
      const response = await fetch(
        `/api/v1/findings/${encodeURIComponent(id)}/acknowledge`,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "X-Homelab-Request": "1",
          },
          body: "{}",
        },
      );
      if (!response.ok)
        throw new Error(
          "Could not acknowledge this finding. Refresh to check its current state.",
        );
      setRevision((x) => x + 1);
      await onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="finding-detail">
      <Button asChild variant="outline">
        <Link to="/">
          <ArrowLeft size={15} /> All findings
        </Link>
      </Button>
      {error && (
        <div role="alert" className="finding-load-error">
          <p className="error">
            {error} {current && "Previously loaded evidence is shown below."}
          </p>
          <Button variant="outline" onClick={() => setRevision((x) => x + 1)}>
            Retry finding
          </Button>
        </div>
      )}
      {!current && !error && (
        <p role="status" className="muted">
          Loading finding evidence…
        </p>
      )}
      {current && (
        <Card className="finding-evidence-card">
          <div className="section-title">
            <h2>{current.title}</h2>
            <Badge
              variant={
                current.state !== "resolved" &&
                ["urgent", "critical"].includes(current.severity)
                  ? "destructive"
                  : "outline"
              }
            >
              {current.severity} · {current.state}
            </Badge>
          </div>
          <div>
            <h3>What was observed</h3>
            <p className="evidence-text">{current.evidence}</p>
            <p className="muted">
              Evidence observation time:{" "}
              {timestamp(current.evidenceObservedUtc)}
            </p>
          </div>
          {current.state === "resolved" ? (
            <div>
              <h3>Resolution evidence</h3>
              <p>
                {current.resolutionEvidence ||
                  "No resolution explanation was recorded."}
              </p>
              <p className="muted">
                Resolved: {timestamp(current.resolvedUtc)}. Resolution of this
                finding does not establish overall NAS health or recoverability.
              </p>
            </div>
          ) : (
            <div>
              <h3>Recommended next step</h3>
              <p>{current.nextStep}</p>
            </div>
          )}
          <dl className="finding-facts">
            <div>
              <dt>First recorded</dt>
              <dd>{timestamp(current.firstSeenUtc)}</dd>
            </div>
            <div>
              <dt>Last recorded</dt>
              <dd>{timestamp(current.lastSeenUtc)}</dd>
            </div>
            <div>
              <dt>Assigned owner</dt>
              <dd>{current.assignedOwner || "Unassigned"}</dd>
            </div>
            <div>
              <dt>Review due</dt>
              <dd>{timestamp(current.reviewUtc)}</dd>
            </div>
          </dl>
          <details>
            <summary>Evidence source & rule</summary>
            <dl className="finding-facts">
              <div>
                <dt>Unit</dt>
                <dd>{current.evidenceUnit || "Unknown"}</dd>
              </div>
              <div>
                <dt>Source</dt>
                <dd>{current.evidenceSource || "Not recorded"}</dd>
              </div>
              <div>
                <dt>Schema</dt>
                <dd>{current.evidenceSchema || "Not recorded"}</dd>
              </div>
              <div>
                <dt>Counter continuity</dt>
                <dd>{current.evidenceEpoch || "Unknown"}</dd>
              </div>
              <div>
                <dt>Rule version</dt>
                <dd>{current.rule}</dd>
              </div>
            </dl>
          </details>
          {current.snoozedUntilUtc && (
            <p className="muted">
              Delivery snoozed until {timestamp(current.snoozedUntilUtc)}.
              Critical findings bypass snoozes.
            </p>
          )}
          <div className="review-actions">
            {(current.rule.startsWith("vdev-counter-increase-v1:") || current.rule.startsWith("resilver-drive-evidence-v1:")) && <Button asChild variant="outline"><Link to="/storage"><HardDrive size={16} />View pool evidence</Link></Button>}
            {current.diskId && (
              <Button asChild variant="outline">
                <Link to={`/storage?disk=${current.diskId}`}>
                  <HardDrive size={16} /> View drive evidence
                </Link>
              </Button>
            )}
            {current.state !== "resolved" && (
              <Button
                variant="outline"
                disabled={busy}
                onClick={() => void acknowledge()}
              >
                <Check size={16} />{" "}
                {busy ? "Saving…" : "Acknowledge for 7 days"}
              </Button>
            )}
          </div>
          {current.state !== "resolved" && (!resolution || resolution.id !== id) && <Button variant="outline" disabled={busy || !current.resolutionToken || !!error} onClick={() => {
            setResolution({ id, token: current.resolutionToken! }); setResolutionMessage("");
          }}>Record resolution</Button>}
          {resolutionMessage && <p role="status" tabIndex={-1} ref={messageRef}>{resolutionMessage}</p>}
          {current.state !== "resolved" && resolution?.id === id && <form onSubmit={async event => {
            event.preventDefault(); const fields = new FormData(event.currentTarget); setBusy(true); setResolutionMessage("");
            try {
              const response = await fetch(`/api/v1/findings/${encodeURIComponent(id)}/resolve`, { method: "POST", signal: AbortSignal.timeout(15000),
                headers: { "Content-Type": "application/json", "X-Homelab-Request": "1" },
                body: JSON.stringify({ resolutionToken: resolution.token, evidence: fields.get("evidence"), reviewedCurrentEvidence: fields.get("reviewed") === "on" }) });
              if (!response.ok) { const body = await response.json().catch(() => ({})); throw new Error(body.error || "Resolution could not be saved. Refresh the finding before retrying."); }
              const saved: Finding = await response.json(); setFinding(saved); setResolution(null); setResolutionMessage("Resolution recorded locally. NAS alerts and settings were not changed."); setRevision(x => x + 1); await onChanged();
            } catch (e) { setResolutionMessage(e instanceof Error ? e.message : "Save interrupted. Refresh to check whether the resolution was recorded."); }
            finally { setBusy(false); }
          }}>
            <h3>Record your investigation</h3>
            <p className="muted">Describe what you checked, the outcome and supporting evidence. This closes this local finding; it does not clear NAS errors, dismiss native alerts, resume collection or prove overall health. New evidence can produce another finding.</p>
            <div className="field"><Label htmlFor="resolution-evidence">Resolution evidence</Label><Textarea id="resolution-evidence" name="evidence" required maxLength={4000} rows={4} /></div>
            <label className="check-field"><input type="checkbox" name="reviewed" required /> I reviewed current evidence and independent recovery readiness.</label>
            <div className="review-actions"><Button disabled={busy}>{busy ? "Saving…" : "Save resolution"}</Button><Button type="button" variant="outline" disabled={busy} onClick={() => { setResolution(null); setResolutionMessage(""); setRevision(x => x + 1); }}>Cancel and refresh</Button></div>
          </form>}
          <p className="muted">
            Acknowledgment records your review locally; it never changes a NAS
            alert or resolves a fault. Observations do not predict a failure
            date.
          </p>
        </Card>
      )}
    </section>
  );
}
