import type { ReactNode } from "react";

export interface PanelProps {
  title: string;
  description?: ReactNode;
  children?: ReactNode;
  actions?: ReactNode;
}
