import {
  Activity,
  Boxes,
  Cpu,
  HardDrive,
  LayoutDashboard,
  Settings,
  ShieldCheck,
  ShoppingBag,
} from "lucide-react";

export const nav = [
  { title: "Overview", path: "/", icon: LayoutDashboard },
  { title: "Storage", path: "/storage", icon: HardDrive },
  { title: "Hardware", path: "/hardware", icon: Cpu },
  { title: "Recovery", path: "/recovery", icon: ShieldCheck },
  { title: "Planning", path: "/planning", icon: ShoppingBag },
  { title: "Inventory", path: "/inventory", icon: Boxes },
  { title: "Activity", path: "/activity", icon: Activity },
  { title: "Settings", path: "/settings", icon: Settings },
];
