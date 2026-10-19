import { useCallback, useEffect, useState } from "react";
import { Page, SetupView, type AccountStatus, type LoginStart, type SetupStep } from "@openstar/components";

import { AdminGate } from "../lib/admin-gate";
import { apiRequest, errorMessage } from "../lib/api";
import { useLoginHelper } from "../lib/login-helper";

function Setup({ adminKey, onRejected }: { adminKey: string; onRejected: (error: unknown) => boolean }) {
  const [account, setAccount] = useState<AccountStatus | null>(null);
  const [login, setLogin] = useState<LoginStart | null>(null);
  const [step, setStep] = useState<SetupStep>("signIn");
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = useCallback(
    async (action: () => Promise<void>) => {
      setIsLoading(true);
      setError(null);
      try {
        await action();
      } catch (caught) {
        if (!onRejected(caught)) {
          setError(errorMessage(caught));
        }
      } finally {
        setIsLoading(false);
      }
    },
    [onRejected],
  );

  useEffect(() => {
    void run(async () => setAccount(await apiRequest<AccountStatus>("/api/setup/account", adminKey)));
  }, [adminKey, run]);

  // Prepare the GM sign-in URL ahead of time so the click opens GM directly.
  useEffect(() => {
    if (step !== "signIn" || login) {
      return;
    }
    void run(async () => setLogin(await apiRequest<LoginStart>("/api/setup/login", adminKey, { method: "POST" })));
  }, [adminKey, login, run, step]);

  const startLogin = () => {
    if (!login || new Date(login.expiresAt).getTime() <= Date.now()) {
      setLogin(null);
      setError("The sign-in link expired. Click the button again.");
      return;
    }

    // Sign in in the user's own browser (a popup window); it passes GM's bot
    // protection and the extension (or a manual paste) returns the redirect.
    const popup = window.open(
      login.authorizationUrl,
      "openstar-gm-login",
      "popup,width=520,height=760,noopener=no",
    );
    if (!popup) {
      setError("The browser blocked the GM sign-in window. Allow pop-ups for this site and try again.");
      return;
    }
    setStep("paste");
  };

  const submitRedirect = (redirect: string) =>
    void run(async () => {
      if (!login) {
        throw new Error("Start the GM sign-in first.");
      }
      const linked = await apiRequest<AccountStatus>("/api/setup/login/complete", adminKey, {
        method: "POST",
        body: { state: login.state, redirect },
      });
      setAccount(linked);
      setLogin(null);
      setStep("done");
    });

  // With the login helper installed, the redirect arrives here instead of being pasted.
  const hasLoginHelper = useLoginHelper((redirect) => {
    if (step !== "paste" || !login || isLoading) {
      return false;
    }
    submitRedirect(redirect);
    return true;
  });

  const unlink = () =>
    void run(async () => {
      if (!window.confirm("Unlink the GM account? API clients will stop working until you sign in again.")) {
        return;
      }
      await apiRequest("/api/setup/account", adminKey, { method: "DELETE" });
      setAccount(await apiRequest<AccountStatus>("/api/setup/account", adminKey));
      setStep("signIn");
    });

  return (
    <SetupView
      step={step}
      account={account}
      isLoading={isLoading}
      error={error}
      hasLoginHelper={hasLoginHelper}
      isLoginReady={login !== null}
      onStartLogin={startLogin}
      onSubmitRedirect={submitRedirect}
      onReopenLogin={() => {
        setLogin(null);
        setStep("signIn");
      }}
      onUnlink={unlink}
    />
  );
}

export default function SetupPage() {
  return (
    <Page
      eyebrow="Setup"
      title="Link your GM account"
      subtitle="GM has no developer sign-in, so OpenStar uses the same sign-in as the myChevrolet app."
    >
      <AdminGate>{(adminKey, onRejected) => <Setup adminKey={adminKey} onRejected={onRejected} />}</AdminGate>
    </Page>
  );
}
