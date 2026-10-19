import { Text, XStack } from "tamagui";

import type { MessageProps } from "./message.props";

const tones = {
  error: { bg: "$red2", border: "$red6", fg: "$red11", icon: "✕" },
  success: { bg: "$green2", border: "$green6", fg: "$green11", icon: "✓" },
  info: { bg: "$color2", border: "$color6", fg: "$color11", icon: "ℹ" },
} as const;

export function Message({ tone = "info", children }: MessageProps) {
  if (!children) {
    return null;
  }

  const palette = tones[tone];

  return (
    <XStack
      alignItems="flex-start"
      gap="$2.5"
      paddingHorizontal="$3.5"
      paddingVertical="$2.5"
      borderRadius="$4"
      borderWidth={1}
      borderColor={palette.border as never}
      backgroundColor={palette.bg as never}
    >
      <Text fontSize={13} fontWeight="700" lineHeight={20} color={palette.fg as never}>
        {palette.icon}
      </Text>
      <Text flex={1} fontSize={14} lineHeight={20} color={palette.fg as never}>
        {children}
      </Text>
    </XStack>
  );
}
