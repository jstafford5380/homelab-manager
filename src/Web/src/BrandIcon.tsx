/** Decorative branding; the adjacent application name supplies the accessible label. */
export function BrandIcon() {
  return <span className="brand-icon" aria-hidden="true">
    <img className="brand-icon-light" src="/brand/health-gauge-icon-light-small.svg" width="36" height="36" alt="" />
    <img className="brand-icon-dark" src="/brand/health-gauge-icon-dark-small.svg" width="36" height="36" alt="" />
  </span>;
}
