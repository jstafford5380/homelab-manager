import { useEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";

const mobileSurfaces = new Set<HTMLElement>();
const previousInert = new Map<HTMLElement, boolean>();
function syncMobileSurfaces() {
  const top = [...mobileSurfaces].at(-1);
  if (!top) {
    for (const [element, inert] of previousInert) element.inert = inert;
    previousInert.clear();
    return;
  }
  for (const element of document.body.children) {
    if (!(element instanceof HTMLElement)) continue;
    if (!previousInert.has(element)) previousInert.set(element, element.inert);
    element.inert = element !== top;
  }
}
export function HardwareSurface({
  title,
  description,
  panel = false,
  close,
  children,
  returnFocus,
}: {
  title: string;
  description?: string;
  panel?: boolean;
  close: () => void;
  children: ReactNode;
  returnFocus?: HTMLElement | null;
}) {
  const [mobile, setMobile] = useState(
    () => matchMedia("(max-width: 700px)").matches,
  );
  const heading = useRef<HTMLHeadingElement>(null);
  const fullpage = useRef<HTMLElement>(null);
  useEffect(() => {
    const q = matchMedia("(max-width: 700px)");
    const change = () => setMobile(q.matches);
    q.addEventListener("change", change);
    return () => q.removeEventListener("change", change);
  }, []);
  useEffect(() => {
    if (mobile) heading.current?.focus();
  }, [mobile]);
  useEffect(() => {
    if (!mobile || !fullpage.current) return;
    const surface = fullpage.current;
    mobileSurfaces.add(surface);
    syncMobileSurfaces();
    return () => {
      mobileSurfaces.delete(surface);
      syncMobileSurfaces();
    };
  }, [mobile]);
  useEffect(
    () => () => {
      requestAnimationFrame(() => returnFocus?.focus());
    },
    [returnFocus],
  );
  if (mobile)
    return createPortal(
      <section
        ref={fullpage}
        className="hw-fullpage"
        aria-label={title}
        onKeyDown={(e) => {
          if (e.key === "Escape") close();
        }}
      >
        <Button variant="outline" onClick={close}>
          ← Back · Close
        </Button>
        <h2 ref={heading} tabIndex={-1}>
          {title}
        </h2>
        {description && <p>{description}</p>}
        {children}
      </section>,
      document.body,
    );
  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) close();
      }}
    >
      <DialogContent
        style={panel ? { translate: "0 0", transform: "none" } : undefined}
        className={`hw-surface ${panel ? "hw-panel" : "hw-form-surface"}`}
        onOpenAutoFocus={(e) => {
          e.preventDefault();
          requestAnimationFrame(() =>
            document
              .querySelector<HTMLElement>('[data-slot="dialog-title"]')
              ?.focus(),
          );
        }}
        aria-describedby={description ? undefined : undefined}
        onCloseAutoFocus={(e) => {
          e.preventDefault();
          returnFocus?.focus();
        }}
      >
        <DialogTitle tabIndex={-1}>{title}</DialogTitle>
        {description && <DialogDescription>{description}</DialogDescription>}
        {children}
      </DialogContent>
    </Dialog>
  );
}
