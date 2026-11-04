import { useCallback, useState } from "react";
export function useRouteDialog(scope: string, initiallyOpen: boolean) {
  const [dialog, setDialog] = useState<{ scope: string; open: boolean } | null>(
    null,
  );
  const open = dialog?.scope === scope ? dialog.open : initiallyOpen;
  const setOpen = useCallback(
    (open: boolean) => setDialog({ scope, open }),
    [scope],
  );
  return [open, setOpen] as const;
}
