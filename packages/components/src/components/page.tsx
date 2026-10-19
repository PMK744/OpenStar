import type { ReactNode } from "react";
import { H1, Paragraph, Text, YStack } from "tamagui";

export function Page({
  eyebrow,
  title,
  subtitle,
  children,
}: {
  eyebrow?: string;
  title: string;
  subtitle?: string;
  children?: ReactNode;
}) {
  return (
    <YStack width="100%" maxWidth={920} marginHorizontal="auto" paddingHorizontal="$5" paddingVertical="$8" gap="$6">
      <YStack gap="$2">
        {eyebrow ? (
          <Text fontSize={12} fontWeight="800" letterSpacing={1.6} textTransform="uppercase" color="$blue11">
            {eyebrow}
          </Text>
        ) : null}
        <H1 fontSize={34} lineHeight={40} fontWeight="800" letterSpacing={-0.5} color="$color12">
          {title}
        </H1>
        {subtitle ? (
          <Paragraph fontSize={16} lineHeight={24} color="$color11" maxWidth={640}>
            {subtitle}
          </Paragraph>
        ) : null}
      </YStack>
      {children}
    </YStack>
  );
}
