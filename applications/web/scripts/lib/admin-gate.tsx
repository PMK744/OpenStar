import { useCallback, useState, type ReactNode } from "react";
import { AdminKeyForm } from "@openstar/components";

import { ApiError, apiRequest, errorMessage, getStoredAdminKey, isManaged, storeAdminKey } from "./api";

/**
 * Renders the admin key prompt until a key is known, then renders the page
 * with it. Pages call `onRejected` when the API refuses the stored key.
 */
export function AdminGate({
  children,
}: {
  children: (adminKey: string, onRejected: (error: unknown) => boolean) => ReactNode;
}) {
  const [adminKey, setAdminKey] = useState(getStoredAdminKey);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const submit = useCallback(async (candidate: string) => {
    setIsLoading(true);
    setError(null);
    try {
      await apiRequest("/api/setup/verify", candidate);
      storeAdminKey(candidate);
      setAdminKey(candidate);
    } catch (caught) {
      setError(errorMessage(caught));
    } finally {
      setIsLoading(false);
    }
  }, []);

  const onRejected = useCallback((caught: unknown) => {
    if (caught instanceof ApiError && (caught.status === 401 || caught.status === 403)) {
      storeAdminKey(null);
      setAdminKey(null);
      setError("The saved admin key no longer works. Enter it again.");
      return true;
    }
    return false;
  }, []);

  // The web app injects the admin key server-side; render straight through.
  if (isManaged) {
    return <>{children("", () => false)}</>;
  }

  return adminKey ? children(adminKey, onRejected) : <AdminKeyForm error={error} isLoading={isLoading} onSubmit={submit} />;
}
