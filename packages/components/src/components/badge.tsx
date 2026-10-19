import type { ReactNode } from "react";
import { Circle, Text, XStack } from "tamagui";

export type BadgeTone = "neutral" | "success" | "warning" | "danger" | "info";

const tones: Record<BadgeTone, { bg: string; fg: string; border: string }> = {
  neutral: { bg: "$color3", fg: "$color11", border: "$color6" },
  success: { bg: "$green3", fg: "$green11", border: "$green6" },
  warning: { bg: "$yellow3", fg: "$yellow11", border: "$yellow7" },
  danger: { bg: "$red3", fg: "$red11", border: "$red6" },
  info: { bg: "$blue3", fg: "$blue11", border: "$blue6" },
};

/** A small status pill, optionally with a leading status dot. */
export function Badge({ tone = "neutral", dot = false, children }: { tone?: BadgeTone; dot?: boolean; children: ReactNode }) {
  const palette = tones[tone];

  return (
    <XStack
      alignItems="center"
      gap="$2"
      paddingHorizontal="$3"
      paddingVertical="$1.5"
      borderRadius={999}
      borderWidth={1}
      borderColor={palette.border as never}
      backgroundColor={palette.bg as never}
    >
      {dot ? <Circle size={7} backgroundColor={palette.fg as never} /> : null}
      <Text fontSize={12} fontWeight="600" letterSpacing={0.2} color={palette.fg as never}>
        {children}
      </Text>
    </XStack>
  );
}
