import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { FormField } from "../../app/FormField";

export function ConnectionEnrollmentDialog({
  open,
  onOpenChange,
  busy,
  error,
  onSubmit,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  busy: boolean;
  error: string;
  onSubmit: (values: Record<string, unknown>) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogTrigger asChild>
        <Button>
          <Plus size={16} /> Connect NAS
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Enroll a NAS</DialogTitle>
        </DialogHeader>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            const fields = Object.fromEntries(
              new FormData(event.currentTarget),
            );
            onSubmit({
              ...fields,
              connectHost: fields.connectHost || null,
              connectPort: fields.connectPort
                ? Number(fields.connectPort)
                : null,
            });
          }}
        >
          <FormField name="name" label="Display name" placeholder="Home NAS" />
          <FormField
            name="apiUri"
            label="Logical secure WebSocket address"
            placeholder="wss://nas.home/websocket"
          />
          <FormField name="apiKey" label="TrueNAS API key" type="password" />
          <div className="field">
            <Label htmlFor="enroll-trusted-ca">
              Trusted public CA certificate (optional)
            </Label>
            <textarea
              id="enroll-trusted-ca"
              name="trustedCaPem"
              rows={4}
              maxLength={20000}
              aria-describedby="enroll-ca-help"
            />
            <p id="enroll-ca-help" className="muted">
              For a private certificate, paste the public CA certificate before
              connecting. Never paste a private key. Leave blank for
              system-trusted certificates.
            </p>
          </div>
          <FormField
            name="connectHost"
            label="Tunnel connection host (optional)"
            required={false}
          />
          <FormField
            name="connectPort"
            label="Tunnel port (optional)"
            type="number"
            required={false}
          />
          <p className="muted">
            TLS checks the logical NAS hostname. Your existing SSH tunnel only
            changes where the connection is opened.
          </p>
          <Button disabled={busy} type="submit">
            Save
          </Button>
          {error && <p className="error">{error}</p>}
        </form>
      </DialogContent>
    </Dialog>
  );
}
