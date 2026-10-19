import { useState } from "react";
import { Button, Input, Separator, Text, XStack, YStack } from "tamagui";

import { Badge, CodeBlock, CopyButton, Message, Panel } from "../components";

import type { KeysProps } from "./keys.props";

function formatDate(value: string | null) {
  return value ? new Date(value).toLocaleString() : "never";
}

export function KeysView({ apiBaseUrl, keys, created, isLoading = false, error, onCreate, onRevoke }: KeysProps) {
  const [name, setName] = useState("");
  const curl = created ? `curl -H "X-Api-Key: ${created.secret}" ${apiBaseUrl}/api/vehicles` : "";

  return (
    <YStack gap="$5">
      <Panel
        title="Create a key"
        description="Client keys can read vehicle data and send commands, but cannot manage the account or other keys. Give each app its own key."
      >
        <XStack gap="$3" flexWrap="wrap">
          <Input flex={1} minWidth={220} placeholder="Name, e.g. Home Assistant" value={name} onChangeText={setName} />
          <Button
            theme="blue"
            disabled={isLoading}
            onPress={() => {
              onCreate(name);
              setName("");
            }}
          >
            Create key
          </Button>
        </XStack>

        {created ? (
          <YStack
            gap="$3"
            padding="$4"
            borderRadius="$5"
            borderWidth={1}
            borderColor="$green6"
            backgroundColor="$green2"
          >
            <Message tone="success">{`Created "${created.key.name}". Copy it now — it won't be shown again.`}</Message>
            <YStack gap="$1.5">
              <XStack alignItems="center" justifyContent="space-between" gap="$3">
                <Text fontSize={12} fontWeight="700" textTransform="uppercase" letterSpacing={1} color="$color11">
                  Key
                </Text>
                <CopyButton value={created.secret} label="Copy key" />
              </XStack>
              <CodeBlock>{created.secret}</CodeBlock>
            </YStack>
            <YStack gap="$1.5">
              <XStack alignItems="center" justifyContent="space-between" gap="$3">
                <Text fontSize={12} fontWeight="700" textTransform="uppercase" letterSpacing={1} color="$color11">
                  Try it
                </Text>
                <CopyButton value={curl} label="Copy command" />
              </XStack>
              <CodeBlock>{curl}</CodeBlock>
            </YStack>
          </YStack>
        ) : null}

        <Message tone="error">{error}</Message>
      </Panel>

      <Panel
        title="Keys"
        actions={keys.length ? <Badge tone="neutral">{`${keys.length} active`}</Badge> : undefined}
        description={keys.length ? undefined : "No client keys yet. Create one above for each app that will call the API."}
      >
        {keys.map((key, index) => (
          <YStack key={key.id} gap="$3">
            {index > 0 ? <Separator /> : null}
            <XStack alignItems="center" justifyContent="space-between" gap="$3" flexWrap="wrap">
              <YStack gap="$1" flex={1} minWidth={220}>
                <Text fontSize={15} fontWeight="600" color="$color12">
                  {key.name}
                </Text>
                <Text fontSize={13} color="$color11">
                  {`${key.prefix}… · created ${formatDate(key.createdAt)} · last used ${formatDate(key.lastUsedAt)}`}
                </Text>
              </YStack>
              <Button size="$3" variant="outlined" theme="red" disabled={isLoading} onPress={() => onRevoke(key.id)}>
                Revoke
              </Button>
            </XStack>
          </YStack>
        ))}
      </Panel>
    </YStack>
  );
}
