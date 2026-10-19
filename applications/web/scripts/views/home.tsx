import { useCallback, useEffect, useState } from "react";
import {
  DashboardView,
  Page,
  type AccountStatus,
  type CommandRecord,
  type Vehicle,
  type VehicleCommandName,
} from "@openstar/components";

import { AdminGate } from "../lib/admin-gate";
import { apiRequest, errorMessage } from "../lib/api";

interface GarageResponse {
  data?: { vehicles?: Vehicle[] };
}

function Dashboard({ adminKey, onRejected }: { adminKey: string; onRejected: (error: unknown) => boolean }) {
  const [account, setAccount] = useState<AccountStatus | null>(null);
  const [vehicles, setVehicles] = useState<Vehicle[]>([]);
  const [selectedVin, setSelectedVin] = useState<string | null>(null);
  const [commands, setCommands] = useState<CommandRecord[]>([]);
  const [result, setResult] = useState<{ title: string; body: string } | null>(null);
  const [busyAction, setBusyAction] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const report = useCallback(
    (caught: unknown) => {
      if (!onRejected(caught)) {
        setError(errorMessage(caught));
      }
    },
    [onRejected],
  );

  const loadCommands = useCallback(
    async () => setCommands(await apiRequest<CommandRecord[]>("/api/commands", adminKey)),
    [adminKey],
  );

  useEffect(() => {
    void (async () => {
      try {
        const status = await apiRequest<AccountStatus>("/api/setup/account", adminKey);
        setAccount(status);
        if (!status.isLinked) {
          return;
        }

        const garage = await apiRequest<GarageResponse>("/api/vehicles", adminKey);
        // Fall back to the VINs in the token if the garage query returns nothing.
        const found = garage.data?.vehicles?.length ? garage.data.vehicles : status.vins.map((vin) => ({ vin }));
        setVehicles(found);
        setSelectedVin(found[0]?.vin ?? null);
        await loadCommands();
      } catch (caught) {
        report(caught);
      }
    })();
  }, [adminKey, loadCommands, report]);

  // Keep the recent-commands list current while anything is in progress.
  useEffect(() => {
    if (!commands.some((command) => command.status === "InProgress")) {
      return;
    }
    const timer = window.setInterval(() => void loadCommands().catch(report), 3000);
    return () => window.clearInterval(timer);
  }, [commands, loadCommands, report]);

  const busy = async (action: string, work: () => Promise<void>) => {
    setBusyAction(action);
    setError(null);
    try {
      await work();
    } catch (caught) {
      report(caught);
    } finally {
      setBusyAction(null);
    }
  };

  const show = (title: string, value: unknown) => setResult({ title, body: JSON.stringify(value, null, 2) });

  return (
    <DashboardView
      account={account}
      vehicles={vehicles}
      selectedVin={selectedVin}
      commands={commands}
      result={result}
      busyAction={busyAction}
      error={error}
      onSelectVin={setSelectedVin}
      onCommand={(command: VehicleCommandName) =>
        void busy(command, async () => {
          if (command === "unlock" || command === "unlock-trunk" || command === "start") {
            if (!window.confirm(`Send "${command}" to the vehicle?`)) {
              return;
            }
          }
          const record = await apiRequest<CommandRecord>(`/api/vehicles/${selectedVin}/commands/${command}`, adminKey, {
            method: "POST",
            body: {},
          });
          show(`Command: ${command}`, record);
          await loadCommands();
        })
      }
      onLocation={() =>
        void busy("location", async () => show("Location", await apiRequest(`/api/vehicles/${selectedVin}/location`, adminKey)))
      }
      onDiagnostics={() =>
        void busy("diagnostics", async () =>
          show("Diagnostics", await apiRequest(`/api/vehicles/${selectedVin}/diagnostics`, adminKey)),
        )
      }
      onRefreshTokens={() =>
        void busy("refresh", async () => setAccount(await apiRequest<AccountStatus>("/api/setup/refresh", adminKey, { method: "POST" })))
      }
    />
  );
}

export default function Home() {
  return (
    <Page eyebrow="OpenStar" title="Dashboard" subtitle="Check the linked account and try commands before wiring up your own app.">
      <AdminGate>{(adminKey, onRejected) => <Dashboard adminKey={adminKey} onRejected={onRejected} />}</AdminGate>
    </Page>
  );
}
