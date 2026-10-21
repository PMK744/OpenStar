// Shapes returned by the OpenStar API, shared by the views and the web app.

export interface AccountStatus {
  isLinked: boolean;
  email: string | null;
  accountNumber: string | null;
  vins: string[];
  linkedAt: string | null;
  refreshTokenExpiresAt: string | null;
  lastError: string | null;
}

export interface LoginStart {
  state: string;
  authorizationUrl: string;
  expiresAt: string;
}

export interface ApiKeySummary {
  id: string;
  name: string;
  prefix: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface CreatedApiKey {
  key: ApiKeySummary;
  secret: string;
}

export type CommandStatus = "InProgress" | "Success" | "Failure" | "Timeout";

export interface CommandRecord {
  id: string;
  vin: string;
  command: string;
  status: CommandStatus;
  createdAt: string;
  completedAt: string | null;
  error: string | null;
  response: unknown;
}

export interface Vehicle {
  vin: string;
  make?: string;
  model?: string;
  year?: string | number;
  nickName?: string | null;
}

export const vehicleCommands = [
  { name: "lock", label: "Lock" },
  { name: "unlock", label: "Unlock" },
  { name: "start", label: "Remote start" },
  { name: "cancel-start", label: "Cancel start" },
  { name: "alert", label: "Honk & flash" },
  { name: "flash-lights", label: "Flash lights" },
  { name: "cancel-alert", label: "Cancel alert" },
  { name: "lock-trunk", label: "Lock trunk" },
  { name: "unlock-trunk", label: "Unlock trunk" },
] as const;

export type VehicleCommandName = (typeof vehicleCommands)[number]["name"];
