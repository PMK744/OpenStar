import { useState } from "react";
import { Button } from "tamagui";

/** Copies text to the clipboard and briefly confirms. */
export function CopyButton({ value, label = "Copy", size = "$2" }: { value: string; label?: string; size?: "$2" | "$3" }) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      // Clipboard may be blocked (e.g. insecure context); the text is still selectable.
    }
  };

  return (
    <Button size={size} variant="outlined" theme={copied ? "green" : undefined} onPress={() => void copy()}>
      {copied ? "Copied" : label}
    </Button>
  );
}
