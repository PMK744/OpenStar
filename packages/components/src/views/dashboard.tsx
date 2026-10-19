import { Button, Separator, Text, XStack, YStack } from "tamagui";

import { Badge, CodeBlock, CopyButton, LinkButton, Message, Panel, type BadgeTone } from "../components";
import { vehicleCommands, type CommandStatus } from "../models";

import type { DashboardProps } from "./dashboard.props";

const statusTones: Record<CommandStatus, BadgeTone> = {
  InProgress: "warning",
  Success: "success",
  Failure: "danger",
  Timeout: "warning",
};

// The common, safety-relevant actions get prominence; the rest sit below.
const primaryCommands = new Set(["lock", "unlock", "start", "cancel-start"]);

function describeVehicle(vehicle: DashboardProps["vehicles"][number]) {
  const name = [vehicle.year, vehicle.make, vehicle.model].filter(Boolean).join(" ");
  return vehicle.nickName || name || vehicle.vin;
}

export function DashboardView({
  account,
  vehicles,
  selectedVin,
  commands,
  result,
  busyAction,
  error,
  onSelectVin,
  onCommand,
  onLocation,
  onDiagnostics,
  onRefreshTokens,
}: DashboardProps) {
  if (account && !account.isLinked) {
    return (
      <Panel
        title="No GM account linked"
        description="Link your GM account before using the dashboard or the API."
      >
        <XStack>
          <LinkButton theme="blue" href="/setup">
            Go to setup
          </LinkButton>
        </XStack>
      </Panel>
    );
  }

  const isBusy = busyAction !== null;
  const primary = vehicleCommands.filter((command) => primaryCommands.has(command.name));
  const secondary = vehicleCommands.filter((command) => !primaryCommands.has(command.name));

  return (
    <YStack gap="$5">
      {account ? (
        <Panel
          title="Account"
          actions={
            <XStack alignItems="center" gap="$3">
              <Badge tone="success" dot>
                Linked
              </Badge>
              <Button size="$3" variant="outlined" disabled={isBusy} onPress={onRefreshTokens}>
                {busyAction === "refresh" ? "Refreshing…" : "Refresh tokens"}
              </Button>
            </XStack>
          }
        >
          <YStack gap="$1">
            <Text fontSize={15} fontWeight="600" color="$color12">
              {account.email ?? "GM account"}
            </Text>
            <Text fontSize={13} color="$color11">
              {`OnStar account ${account.accountNumber ?? "unknown"} · ${vehicles.length || account.vins.length} vehicle(s)`}
            </Text>
            <Text fontSize={13} color="$color11" marginTop="$1">
              {account.refreshTokenExpiresAt
                ? `Sign-in valid until ${new Date(account.refreshTokenExpiresAt).toLocaleString()} — extended on each refresh.`
                : "GM did not report when the sign-in expires."}
            </Text>
          </YStack>
          <Message tone="error">{account.lastError}</Message>
        </Panel>
      ) : null}

      <Panel title="Vehicle" description={vehicles.length ? "Pick a vehicle, then send a command." : "Loading vehicles…"}>
        <XStack gap="$2.5" flexWrap="wrap">
          {vehicles.map((vehicle) => {
            const selected = vehicle.vin === selectedVin;
            return (
              <Button
                key={vehicle.vin}
                size="$3"
                theme={selected ? "blue" : undefined}
                variant={selected ? undefined : "outlined"}
                onPress={() => onSelectVin(vehicle.vin)}
              >
                {describeVehicle(vehicle)}
              </Button>
            );
          })}
        </XStack>

        {selectedVin ? (
          <YStack gap="$3.5">
            <Separator marginVertical="$1" />
            <YStack gap="$2">
              <Text fontSize={12} fontWeight="800" textTransform="uppercase" letterSpacing={1.2} color="$color11">
                Commands
              </Text>
              <XStack gap="$2.5" flexWrap="wrap">
                {primary.map((command) => (
                  <Button key={command.name} size="$3" theme="blue" disabled={isBusy} onPress={() => onCommand(command.name)}>
                    {busyAction === command.name ? "Sending…" : command.label}
                  </Button>
                ))}
              </XStack>
              <XStack gap="$2.5" flexWrap="wrap">
                {secondary.map((command) => (
                  <Button key={command.name} size="$3" variant="outlined" disabled={isBusy} onPress={() => onCommand(command.name)}>
                    {busyAction === command.name ? "Sending…" : command.label}
                  </Button>
                ))}
              </XStack>
            </YStack>

            <YStack gap="$2">
              <Text fontSize={12} fontWeight="800" textTransform="uppercase" letterSpacing={1.2} color="$color11">
                Status
              </Text>
              <XStack gap="$2.5" flexWrap="wrap">
                <Button size="$3" variant="outlined" disabled={isBusy} onPress={onLocation}>
                  {busyAction === "location" ? "Locating…" : "Get location"}
                </Button>
                <Button size="$3" variant="outlined" disabled={isBusy} onPress={onDiagnostics}>
                  {busyAction === "diagnostics" ? "Loading…" : "Diagnostics"}
                </Button>
              </XStack>
            </YStack>
          </YStack>
        ) : null}

        <Message tone="error">{error}</Message>
      </Panel>

      {result ? (
        <Panel title={result.title} actions={<CopyButton value={result.body} label="Copy JSON" />}>
          <CodeBlock>{result.body}</CodeBlock>
        </Panel>
      ) : null}

      <Panel
        title="Recent commands"
        description={commands.length ? undefined : "No commands sent since the API started."}
      >
        {commands.map((command, index) => (
          <YStack key={command.id} gap="$2.5">
            {index > 0 ? <Separator /> : null}
            <XStack alignItems="center" justifyContent="space-between" gap="$3" flexWrap="wrap">
              <YStack gap="$1" flex={1} minWidth={200}>
                <Text fontSize={14} fontWeight="600" color="$color12">
                  {command.command}
                </Text>
                <Text fontSize={12} color="$color11">
                  {`${command.vin} · ${new Date(command.createdAt).toLocaleTimeString()}${command.error ? ` · ${command.error}` : ""}`}
                </Text>
              </YStack>
              <Badge tone={statusTones[command.status]} dot={command.status === "InProgress"}>
                {command.status === "InProgress" ? "In progress" : command.status}
              </Badge>
            </XStack>
          </YStack>
        ))}
      </Panel>
    </YStack>
  );
}
