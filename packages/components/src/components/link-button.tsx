import type { ComponentProps } from "react";
import { Button } from "tamagui";

/** A button that navigates to another page of the setup UI. */
export function LinkButton({ href, ...props }: ComponentProps<typeof Button> & { href: string }) {
  return <Button {...props} onPress={() => window.location.assign(href)} />;
}
