import { BrandIcon } from "../components/BrandIcon";
import { Link } from "react-router";
import { LogOut } from "lucide-react";
import { nav } from "./navigation";

export function AppNavigation({
  page,
  issueCount,
  onLogout,
}: {
  page: string;
  issueCount: number;
  onLogout: () => void;
}) {
  return (
    <aside>
      <Link className="brand" to="/">
        <BrandIcon />
        <span>
          homelab<span className="brand-light"> / control</span>
        </span>
      </Link>
      <p className="nav-label">WORKSPACE</p>
      <nav aria-label="Main navigation">
        {nav.map((item) => (
          <Link
            key={item.title}
            className={page === item.title ? "active" : ""}
            to={item.title === "Settings" ? "/settings/connections" : item.path}
            aria-label={
              item.title === "Overview" && issueCount > 0
                ? `Overview ${issueCount} open ${issueCount === 1 ? "finding" : "findings"}`
                : item.title
            }
            aria-current={page === item.title ? "page" : undefined}
          >
            <item.icon size={18} />
            {item.title}
            {item.title === "Overview" && issueCount > 0 && (
              <span className="nav-count" aria-hidden="true">
                {` ${issueCount}`}
              </span>
            )}
          </Link>
        ))}
      </nav>
      <button className="logout" onClick={onLogout}>
        <LogOut size={16} /> Sign out
      </button>
    </aside>
  );
}
