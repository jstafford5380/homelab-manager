import { SelectField, SelectOption } from "@/components/SelectField";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export function Field({
  name,
  label,
  value = "",
  type = "text",
  required = false,
}: {
  name: string;
  label: string;
  value?: string | number;
  type?: string;
  required?: boolean;
}) {
  return (
    <div className="field">
      <Label htmlFor={"inventory-" + name}>{label}</Label>
      <Input
        id={"inventory-" + name}
        name={name}
        defaultValue={value}
        type={type}
        required={required}
      />
    </div>
  );
}
export function Choice({
  name,
  label,
  values,
  value,
}: {
  name: string;
  label: string;
  values: string[];
  value?: string;
}) {
  return (
    <div className="field">
      <Label htmlFor={"inventory-" + name}>{label}</Label>
      <SelectField id={"inventory-" + name} name={name} defaultValue={value}>
        {values.map((v) => (
          <SelectOption key={v}>{v}</SelectOption>
        ))}
      </SelectField>
    </div>
  );
}
