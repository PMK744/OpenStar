import type { AccountStatus } from "../models";

export type SetupStep = "signIn" | "paste" | "done";

export interface SetupProps {
  step: SetupStep;
  account: AccountStatus | null;
  isLoading?: boolean;
  /** The GM sign-in URL has been prepared and can be opened. */
  isLoginReady?: boolean;
  error?: string | null;
  /** The OpenStar login helper extension is installed and will send the redirect itself. */
  hasLoginHelper?: boolean;
  onStartLogin: () => void;
  onSubmitRedirect: (redirect: string) => void;
  onReopenLogin: () => void;
  onUnlink: () => void;
}
