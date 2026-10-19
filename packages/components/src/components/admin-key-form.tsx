import { useState } from "react";
import { Button, Input, XStack } from "tamagui";

import { Message } from "./message";
import { Panel } from "./panel";

import type { AdminKeyFormProps } from "./admin-key-form.props";

export function AdminKeyForm({ error, isLoading = false, onSubmit }: AdminKeyFormProps) {
  const [value, setValue] = useState("");

  return (
    <Panel
      title="Enter your admin key"
      description="The OpenStar API printed an admin key (osa_…) to its log the first time it started. It is stored in this browser only."
    >
      <XStack gap="$3" flexWrap="wrap">
        <Input
          flex={1}
          minWidth={240}
          placeholder="osa_…"
          value={value}
          onChangeText={setValue}
          secureTextEntry
          onSubmitEditing={() => value && onSubmit(value.trim())}
        />
        <Button theme="blue" disabled={!value || isLoading} onPress={() => onSubmit(value.trim())}>
          {isLoading ? "Checking…" : "Continue"}
        </Button>
      </XStack>
      <Message tone="error">{error}</Message>
    </Panel>
  );
}
