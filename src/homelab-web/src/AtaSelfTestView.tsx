export type SelfTestLog = {
  source: string; coverage: string; quality: string; revision: number | null; reportedCount: number | null;
  entries: { position: number; typeCode: number | null; typeText: string | null; statusCode: number | null; statusText: string | null; result: string; remainingPercent: number | null; lifetimeHours: number | null; firstErrorLba: number | null; reportedPassed: boolean | null }[];
};
export default function AtaSelfTestView({ logs }: { logs: SelfTestLog[] | null | undefined }) {
  return <details className="report-reading">
    <summary>Recorded ATA self-tests</summary>
    <p className="muted">Existing report entries only. No test is started. Lifetime hours are a device counter, not a calendar date; entries may wrap, repeat across logs or be incomplete. Completed tests do not prove current health or recovery readiness.</p>
    {!logs?.length && <p>No supported self-test log was retained in this report. This does not mean no tests have run.</p>}
    {logs?.map(log => <section key={log.source} aria-label={log.source}>
      <h4>{log.source.endsWith(".extended") ? "Extended log" : "Standard log"}</h4>
      <p>{log.coverage} · {log.quality} · Reported count: {log.reportedCount ?? "unknown"}</p>
      <small>Source: {log.source} · Revision {log.revision ?? "unknown"}</small>
      {log.entries.map(entry => <article className="planning-stock" key={entry.position}>
        <strong>Entry {entry.position} · {entry.result}</strong>
        <p>{entry.typeText || "Unknown test type"} · Lifetime hours: {entry.lifetimeHours?.toLocaleString() ?? "unknown"}</p>
        <p>Remaining: {entry.remainingPercent === null ? "unknown" : `${entry.remainingPercent}%`} · First error LBA: {entry.firstErrorLba?.toLocaleString() ?? "unknown / not reported"}</p>
        <details><summary>Source status details</summary><p>{entry.statusText || "Status text not reported"}</p><small>Type code {entry.typeCode ?? "unknown"}; status byte {entry.statusCode ?? "unknown"}; source passed flag {entry.reportedPassed === null ? "unknown" : String(entry.reportedPassed)}. Completion is interpreted from the status byte, not this flag.</small></details>
      </article>)}
    </section>)}
  </details>;
}
