import { useCallback, useEffect, useState, type ReactNode } from "react";
import { AdminKeyForm } from "@openstar/components";

import { ApiError, apiRequest, errorMessage, getStoredAdminKey, isManaged, setAdminKey, storeAdminKey } from "./api";

/**
 * Holds the admin key for the API client. It prompts until a valid key is known
 * (or runs straight through in managed mode, where the server injects the key),
 * then renders the page. Pages call `onRejected` when the API refuses the key.
 */
export function AdminGate({
  children,
}: {
  children: (onRejected: (error: unknown) => boolean) => ReactNode;
}) {
  const [key, setKey] = useState<string | null>(() => (isManaged ? null : getStoredAdminKey()));
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  // Keep the API client's key in sync with the gate.
  useEffect(() => setAdminKey(key), [key]);

  const submit = useCallback(async (candidate: string) => {
    setIsLoading(true);
    setError(null);
    setAdminKey(candidate);
    try {
      await apiRequest("/api/setup/verify");
      storeAdminKey(candidate);
      setKey(candidate);
    } catch (caught) {
      setAdminKey(null);
      setError(errorMessage(caught));
    } finally {
      setIsLoading(false);
    }
  }, []);

  const onRejected = useCallback((caught: unknown) => {
    if (caught instanceof ApiError && (caught.status === 401 || caught.status === 403)) {
      storeAdminKey(null);
      setKey(null);
      setError("The saved admin key no longer works. Enter it again.");
      return true;
    }
    return false;
  }, []);

  if (isManaged) {
    return <>{children(onRejected)}</>;
  }

  return key ? children(onRejected) : <AdminKeyForm error={error} isLoading={isLoading} onSubmit={submit} />;
}
