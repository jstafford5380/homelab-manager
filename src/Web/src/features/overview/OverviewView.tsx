import { useOverviewPlanning } from "./useOverviewPlanning";
import { Plus, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { ScanOverview } from "../storage/ScanOverview";
import type { Snapshot } from "../../app/overviewSnapshot";
import { buildCondition } from "./overviewModel";
import { OverviewCondition } from "./OverviewCondition";
import { OverviewFindings } from "./OverviewFindings";
import { OverviewSystems } from "./OverviewSystems";
import { OverviewUpcoming } from "./OverviewUpcoming";
import { OverviewReadiness } from "./OverviewReadiness";
import { useClock } from "../../lib/useClock";

export function OverviewView({
  snapshot,
  refreshRevision,
  busy,
  onFindingAction,
  onConnect,
}: {
  snapshot: Snapshot;
  refreshRevision: number;
  busy: boolean;
  onFindingAction: (path: string, body: unknown) => void;
  onConnect: () => void;
}) {
  const now = useClock(30000);
  const planning = useOverviewPlanning(refreshRevision);
  const condition = buildCondition(snapshot, planning, now);
  const scanActive = (snapshot.scanReports?.length ?? 0) > 0;
  return (
    <div className="ov">
      <OverviewCondition
        condition={condition}
        investment={snapshot.investment}
        systemCount={snapshot.connections.length}
        now={now}
      />
      {snapshot.connections.length === 0 ? (
        <>
          <Card className="empty">
            <ShieldCheck size={32} />
            <h3>Meet your NAS</h3>
            <p>
              Connect your NAS to see storage topology, disk identity, and
              collection coverage. Missing data will stay visibly unknown.
            </p>
            <Button onClick={onConnect}>
              <Plus size={16} /> Connect a NAS
            </Button>
          </Card>
          {scanActive && <ScanOverview reports={snapshot.scanReports} />}
        </>
      ) : (
        <div className="ov-columns">
          <div className="ov-column">
            {!!condition.issues.length && (
              <OverviewFindings
                issues={condition.issues}
                connections={snapshot.connections}
                now={now}
                busy={busy}
                onFindingAction={onFindingAction}
              />
            )}
            {scanActive && <ScanOverview reports={snapshot.scanReports} />}
            <OverviewSystems
              snapshot={snapshot}
              issues={condition.issues}
              now={now}
            />
          </div>
          <div className="ov-column ov-rail">
            <OverviewUpcoming
              snapshot={snapshot}
              issues={condition.issues}
              planning={planning}
              now={now}
            />
            <OverviewReadiness
              plans={snapshot.plans}
              scanReports={snapshot.scanReports}
              planning={planning}
            />
          </div>
        </div>
      )}
    </div>
  );
}
