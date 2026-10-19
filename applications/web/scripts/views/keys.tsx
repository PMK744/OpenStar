import { useCallback, useEffect, useState } from "react";
import { KeysView, Page, type ApiKeySummary, type CreatedApiKey } from "@openstar/components";

import { AdminGate } from "../lib/admin-gate";
import { apiRequest, clientApiBaseUrl, errorMessage } from "../lib/api";

function Keys({ adminKey, onRejected }: { adminKey: string; onRejected: (error: unknown) => boolean }) {
  const [keys, setKeys] = useState<ApiKeySummary[]>([]);
  const [created, setCreated] = useState<CreatedApiKey | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const run = useCallback(
    async (action: () => Promise<void>) => {
      setIsLoading(true);
      setError(null);
      try {
        await action();
        setKeys(await apiRequest<ApiKeySummary[]>("/api/keys", adminKey));
      } catch (caught) {
        if (!onRejected(caught)) {
          setError(errorMessage(caught));
        }
      } finally {
        setIsLoading(false);
      }
    },
    [adminKey, onRejected],
  );

  useEffect(() => {
    void run(async () => undefined);
  }, [run]);

  return (
    <KeysView
      apiBaseUrl={clientApiBaseUrl}
      keys={keys}
      created={created}
      isLoading={isLoading}
      error={error}
      onCreate={(name) =>
        void run(async () => setCreated(await apiRequest<CreatedApiKey>("/api/keys", adminKey, { method: "POST", body: { name } })))
      }
      onRevoke={(id) =>
        void run(async () => {
          if (window.confirm("Revoke this key? Apps using it will be rejected immediately.")) {
            await apiRequest(`/api/keys/${encodeURIComponent(id)}`, adminKey, { method: "DELETE" });
            setCreated(null);
          }
        })
      }
    />
  );
}

export default function KeysPage() {
  return (
    <Page eyebrow="OpenStar" title="API keys" subtitle={`Apps call the OpenStar API at ${clientApiBaseUrl} with a key in the X-Api-Key header.`}>
      <AdminGate>{(adminKey, onRejected) => <Keys adminKey={adminKey} onRejected={onRejected} />}</AdminGate>
    </Page>
  );
}
