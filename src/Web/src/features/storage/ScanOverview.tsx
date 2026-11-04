import {useEffect,useState} from "react";
import {Link} from "react-router";
import {Card} from "@/components/ui/card";
import {Badge} from "@/components/ui/badge";
import {useDisplayFormat} from "../../lib/displayPreferences";
import {ScanTiming} from "./ScanTiming";
import type {ScanReport} from "./scanModel";
export function ScanOverview({ reports }: { reports?: ScanReport[] }) {
  const { time } = useDisplayFormat();
  const [now, setNow] = useState(Date.now);
  useEffect(() => { const timer = setInterval(() => setNow(Date.now()), 10000); return () => clearInterval(timer); }, []);
  return <section aria-label="Scrub and resilver reports" style={{ marginBottom: 28 }}>
    <div className="section-title scan-overview-heading"><h2>Scrub & resilver reports</h2><Link to="/storage">Review storage history</Link></div>
    {reports?.length ? reports.map(report => {
      const fresh = report.freshness === "fresh" && report.validUntilUtc !== null && Date.parse(report.validUntilUtc) >= now;
      return <Card className="scan-report" key={`${report.connectionId}:${report.poolGuid}`}>
        <div className="section-title"><h3>{report.nasName} · {report.poolName || report.poolGuid}</h3><Badge variant="outline">{fresh ? "Recent source report" : "Last-known report"}</Badge></div>
        <p><strong>{report.function} · {report.scanPausedUtc ? "PAUSED (source report)" : report.state}</strong></p>
        <p>{report.percentage === null ? "Progress unknown" : `Reported progress: ${report.percentage.toFixed(1)}%`} · {report.errors === null ? "Scan errors unknown" : `Reported scan errors: ${report.errors}`}</p>
        <p className="muted">{fresh ? report.explanation : "Current operation state is unverified. This recorded scan may have completed or changed; check source coverage."}</p>
        <ScanTiming scan={report} />
        <small>Pool read: {time(report.fetchedUtc)} · Coverage: {fresh ? "fresh" : report.freshness === "fresh" ? "stale" : report.freshness}</small>
      </Card>;
    }) : <p className="muted">{reports === undefined ? "Scan summary unavailable. Review storage history and collection coverage." : "No in-progress scan is recorded in the latest retained pool snapshots. This does not establish that all pools were checked; review collection coverage."}</p>}
    <p className="muted">These are NAS-reported observations. Homelab Control does not start, pause or stop scans, and does not infer a completion time.</p>
  </section>;
}
