import { Text } from "tamagui";

export const monospace = { fontFamily: "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace" };

const preformatted = { ...monospace, whiteSpace: "pre-wrap", wordBreak: "break-all" } as const;

export function CodeBlock({ children }: { children: string }) {
  return (
    <Text
      render="pre"
      style={preformatted}
      fontSize={12}
      lineHeight={18}
      color="$color12"
      backgroundColor="$color1"
      borderRadius="$4"
      borderWidth={1}
      borderColor="$borderColor"
      padding="$3"
      overflow="scroll"
      maxHeight={360}
    >
      {children}
    </Text>
  );
}
