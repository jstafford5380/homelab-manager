import { BrandIcon } from "../components/BrandIcon";
import { ArrowUpRight, Database } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import type { Session } from "./useAppSession";
import { FormField } from "./FormField";

export function LoginScreen({
  session,
  error,
  canRetry,
  busy,
  onRetry,
  onSubmit,
}: {
  session: Session | null;
  error: string;
  canRetry: boolean;
  busy: boolean;
  onRetry: () => void;
  onSubmit: (values: Record<string, FormDataEntryValue>) => void;
}) {
  if (!session)
    return (
      <div className="login">
        <Database />
        <h1>Homelab Control</h1>
        <p>Connecting to your control service…</p>
        {error && <p role="alert">{error}</p>}
        {canRetry && (
          <Button variant="outline" onClick={() => onRetry()}>
            Retry connection
          </Button>
        )}
      </div>
    );
  return (
    <div className="login">
      <BrandIcon />
      <p className="eyebrow">YOUR INFRASTRUCTURE, UNDERSTOOD</p>
      <h1>Homelab Control</h1>
      <p>A clearer picture of your storage. A plan for what comes next.</p>
      <Card className="login-card">
        <h2>
          {session.setupRequired ? "Create your owner account" : "Welcome back"}
        </h2>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            const values = Object.fromEntries(new FormData(e.currentTarget));
            onSubmit(values);
          }}
        >
          <FormField name="username" label="Username" />
          <FormField name="password" label="Password" type="password" />
          {session.setupRequired && (
            <>
              <FormField
                name="setupToken"
                label="Server setup token"
                type="password"
              />
              <p className="muted">
                Use the token configured on your control service. Choose a
                password of at least 9 characters.
              </p>
            </>
          )}
          <Button disabled={busy} className="w-full">
            {session.setupRequired ? "Create account" : "Sign in"}{" "}
            <ArrowUpRight size={16} />
          </Button>
          {error && (
            <p role="alert" className="error">
              {error}
            </p>
          )}
        </form>
      </Card>
    </div>
  );
}
