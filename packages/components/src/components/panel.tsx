import { Paragraph, Text, XStack, YStack } from "tamagui";

import type { PanelProps } from "./panel.props";

const shadow = {
  boxShadow: "0 1px 2px rgba(0,0,0,0.25), 0 10px 30px rgba(0,0,0,0.18)",
} as const;

export function Panel({ title, description, children, actions }: PanelProps) {
  return (
    <YStack
      render="section"
      width="100%"
      borderRadius="$7"
      borderWidth={1}
      borderColor="$borderColor"
      backgroundColor="$color2"
      padding="$5"
      gap="$3.5"
      style={shadow}
    >
      {title || actions ? (
        <XStack alignItems="center" justifyContent="space-between" gap="$3" flexWrap="wrap">
          {title ? (
            <Text fontSize={18} fontWeight="700" letterSpacing={-0.2} color="$color12">
              {title}
            </Text>
          ) : (
            <YStack />
          )}
          {actions}
        </XStack>
      ) : null}
      {description ? (
        <Paragraph fontSize={14} lineHeight={21} color="$color11">
          {description}
        </Paragraph>
      ) : null}
      {children}
    </YStack>
  );
}
