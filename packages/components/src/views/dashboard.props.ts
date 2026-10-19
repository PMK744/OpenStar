import type { AccountStatus, CommandRecord, Vehicle, VehicleCommandName } from "../models";

export interface DashboardProps {
  account: AccountStatus | null;
  vehicles: Vehicle[];
  selectedVin: string | null;
  commands: CommandRecord[];
  result: { title: string; body: string } | null;
  busyAction: string | null;
  error?: string | null;
  onSelectVin: (vin: string) => void;
  onCommand: (command: VehicleCommandName) => void;
  onLocation: () => void;
  onDiagnostics: () => void;
  onRefreshTokens: () => void;
}
