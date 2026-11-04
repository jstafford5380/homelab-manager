import { when } from "./overviewModel";
import { HoverTip } from "@/components/HoverTip";
import { useDisplayFormat } from "../../lib/displayPreferences";

export function OverviewTime({ value, now }: { value: string; now: number }) {
  const { time } = useDisplayFormat();
  return (
    <HoverTip content={time(value)}>
      <time dateTime={value} tabIndex={0}>
        {when(value, now)}
      </time>
    </HoverTip>
  );
}
