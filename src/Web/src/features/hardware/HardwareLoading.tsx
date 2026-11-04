export function HardwareLoading() {
  return (
    <div
      role="group"
      aria-label="Loading hardware"
      aria-busy="true"
      className="hw-loading"
    >
      <div className="hw-loading-tabs" />
      <div className="hw-facts">
        {[1, 2, 3, 4, 5].map((n) => (
          <div key={n} />
        ))}
      </div>
      <div className="hw-metrics">
        {[1, 2, 3, 4].map((n) => (
          <div key={n} />
        ))}
      </div>
      <div className="hw-loading-inventory" />
    </div>
  );
}
