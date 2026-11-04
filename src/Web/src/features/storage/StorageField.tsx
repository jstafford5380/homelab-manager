import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export function StorageField({
  name,
  label,
  value = "",
  type = "text",
  required = false,
}: {
  name: string;
  label: string;
  value?: string;
  type?: string;
  required?: boolean;
}) {
  return (
    <div className="field">
      <Label htmlFor={"storage-" + name}>{label}</Label>
      <Input
        id={"storage-" + name}
        name={name}
        type={type}
        defaultValue={value}
        required={required}
        step={type === "number" ? "0.01" : undefined}
      />
    </div>
  );
}
