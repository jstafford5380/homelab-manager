import { SelectField, SelectOption } from "@/components/SelectField";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export function Field({
  name,
  label,
  type = "text",
  value = "",
  required = true,
}: {
  name: string;
  label: string;
  type?: string;
  value?: string | number;
  required?: boolean;
}) {
  return (
    <div className="field">
      <Label htmlFor={"recovery-" + name}>{label}</Label>
      <Input
        id={"recovery-" + name}
        name={name}
        type={type}
        defaultValue={value}
        required={required}
      />
    </div>
  );
}
export function Select({
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
      <Label htmlFor={"recovery-" + name}>{label}</Label>
      <SelectField id={"recovery-" + name} name={name} defaultValue={value}>
        {values.map((v) => (
          <SelectOption key={v}>{v}</SelectOption>
        ))}
      </SelectField>
    </div>
  );
}
