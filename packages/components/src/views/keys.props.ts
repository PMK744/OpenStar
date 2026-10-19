import type { ApiKeySummary, CreatedApiKey } from "../models";

export interface KeysProps {
  apiBaseUrl: string;
  keys: ApiKeySummary[];
  created: CreatedApiKey | null;
  isLoading?: boolean;
  error?: string | null;
  onCreate: (name: string) => void;
  onRevoke: (id: string) => void;
}
