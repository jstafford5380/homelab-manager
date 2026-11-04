export function PlanningSkeleton({ rows = 1 }: { rows?: number }) {
  return (
    <div
      role="group"
      aria-busy="true"
      aria-label="Loading drive reviews"
      className="planning-skeletons"
    >
      {Array.from({ length: rows }, (_, i) => (
        <div className="planning-skeleton" key={i}>
          <span />
          <span />
        </div>
      ))}
    </div>
  );
}
