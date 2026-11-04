import { Link } from "react-router";
import { Package, Plus } from "lucide-react";
import { Card } from "@/components/ui/card";
import { Button } from "@/components/ui/button";

export function PlanningOnboarding({ onAdd }: { onAdd: () => void }) {
  return (
    <div className="planning-onboarding">
      {[
        [
          "Connect a NAS",
          "Drive reviews appear after the first inventory. They flag evidence, warranty dates and drive age.",
          "/settings/connections",
          "Connect a NAS",
        ],
        [
          "Record your spares",
          "Add spare drives in Inventory so you can check which drives they fit.",
          "/inventory",
          "Open Inventory",
        ],
        [
          "Add a plan",
          "Track a purchase, replacement or maintenance task with a budget and a date.",
          null,
          "Add plan",
        ],
      ].map(([title, copy, to, action]) => (
        <Card key={title} className="planning-onboard">
          <Package className="tone-info planning-tile" />
          <h2>{title}</h2>
          <p>{copy}</p>
          {to ? (
            <Button asChild variant="outline">
              <Link to={to}>{action}</Link>
            </Button>
          ) : (
            <Button variant="outline" onClick={() => onAdd()}>
              <Plus size={14} />
              {action}
            </Button>
          )}
        </Card>
      ))}
    </div>
  );
}
