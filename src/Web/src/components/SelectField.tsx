import {
  Children,
  Fragment,
  isValidElement,
  useEffect,
  useId,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

type OptionProps = {
  value?: string | number;
  disabled?: boolean;
  children?: ReactNode;
};
export function SelectOption(_: OptionProps) {
  return null;
}
function text(node: ReactNode): string {
  return Children.toArray(node)
    .map((child) =>
      isValidElement<{ children?: ReactNode }>(child)
        ? text(child.props.children)
        : String(child),
    )
    .join("");
}
function options(children: ReactNode): OptionProps[] {
  return Children.toArray(children).flatMap((child) => {
    if (!isValidElement<OptionProps>(child)) return [];
    return child.type === Fragment
      ? options(child.props.children)
      : [child.props];
  });
}
export function SelectField({
  children,
  value,
  defaultValue,
  onValueChange,
  name,
  required,
  disabled,
  ...trigger
}: {
  children?: ReactNode;
  value?: string | number;
  defaultValue?: string | number;
  onValueChange?: (value: string) => void;
  name?: string;
  required?: boolean;
  disabled?: boolean;
} & Omit<
  React.ComponentProps<typeof SelectTrigger>,
  "value" | "defaultValue" | "onChange" | "children" | "onValueChange"
>) {
  const items = options(children);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const errorId = useId();
  const [invalid, setInvalid] = useState(false);
  useEffect(() => {
    if (invalid) triggerRef.current?.focus();
  }, [invalid]);
  const optionValue = (item: OptionProps) =>
    String(item.value ?? text(item.children));
  const [selected, setSelected] = useState(
    String(
      defaultValue ??
        items.find((item) => !item.disabled)?.value ??
        (items[0] ? optionValue(items[0]) : ""),
    ),
  );
  const current = value === undefined ? selected : String(value);
  const placeholder = items.find((item) => optionValue(item) === "")?.children;
  const emptyValue = "__homelab_empty__";
  const allowEmpty =
    !required &&
    items.some((item) => optionValue(item) === "" && !item.disabled);
  return (
    <div className="contents" onInvalidCapture={event => {
      event.preventDefault();
      setInvalid(true);
      triggerRef.current?.focus();
    }}>
      <Select
        value={current === "" && allowEmpty ? emptyValue : current}
        name={allowEmpty ? undefined : name}
        required={required}
        disabled={disabled}
        onValueChange={(selectedValue) => {
          const next = selectedValue === emptyValue ? "" : selectedValue;
          setSelected(next);
          setInvalid(false);
          onValueChange?.(next);
        }}
      >
        <SelectTrigger {...trigger} ref={triggerRef} aria-invalid={invalid || undefined} aria-describedby={invalid ? errorId : trigger["aria-describedby"]}>
          <SelectValue placeholder={placeholder ?? "Choose an option"} />
        </SelectTrigger>
        <SelectContent>
          {items
            .filter((item) => optionValue(item) !== "" || allowEmpty)
            .map((item) => (
              <SelectItem
                data-value={optionValue(item)}
                key={optionValue(item)}
                value={optionValue(item) || emptyValue}
                disabled={item.disabled}
              >
                {item.children}
              </SelectItem>
            ))}
        </SelectContent>
      </Select>
      {allowEmpty && name && (
        <input type="hidden" name={name} value={current} disabled={disabled} />
      )}
      {invalid && <p id={errorId} role="alert" className="text-sm" style={{ color: "var(--tone-bad)" }}>Choose an option before saving.</p>}
    </div>
  );
}
