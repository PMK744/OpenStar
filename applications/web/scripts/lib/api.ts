const adminKeyStorageKey = "openstar.adminKey";

/**
 * The setup UI calls the API through this web app (same origin), which proxies
 * to the backend. That way the browser only needs the one port it already
 * reached, not the API's port.
 */
export const apiBaseUrl = "";

/**
 * The address external API clients use to reach the API directly. It defaults
 * to this host's port 5290; the `openstar-api` meta tag overrides it.
 */
export const clientApiBaseUrl = (() => {
  const configured = document.querySelector<HTMLMetaElement>('meta[name="openstar-api"]')?.content.trim();
  return (configured || `${window.location.protocol}//${window.location.hostname}:5290`).replace(/\/$/, "");
})();

/**
 * Managed mode: the web app injects the admin key into proxied requests itself,
 * so the UI skips the admin-key prompt. Set by the all-in-one container.
 */
export const isManaged =
  document.querySelector<HTMLMetaElement>('meta[name="openstar-managed"]')?.content.trim() === "true";

export function getStoredAdminKey(): string | null {
  try {
    return window.localStorage.getItem(adminKeyStorageKey);
  } catch {
    return null;
  }
}

export function storeAdminKey(adminKey: string | null) {
  try {
    if (adminKey) {
      window.localStorage.setItem(adminKeyStorageKey, adminKey);
    } else {
      window.localStorage.removeItem(adminKeyStorageKey);
    }
  } catch {
    // Storage may be unavailable (private mode); the key then lasts for this page only.
  }
}

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}

export async function apiRequest<T>(
  path: string,
  adminKey: string,
  init: { method?: string; body?: unknown } = {},
): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl}${path}`, {
      method: init.method ?? "GET",
      headers: {
        "X-Api-Key": adminKey,
        ...(init.body === undefined ? {} : { "Content-Type": "application/json" }),
      },
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
    });
  } catch {
    throw new ApiError("Could not reach the OpenStar app. Is it running?", 0);
  }

  if (!response.ok) {
    let message = `Request failed (${response.status}).`;
    try {
      const problem = (await response.json()) as { title?: string; detail?: string };
      message = [problem.title, problem.detail].filter(Boolean).join(": ") || message;
    } catch {
      // Not a problem-details body; keep the status message.
    }
    if (response.status === 401) {
      message = "The admin key was rejected.";
    }
    throw new ApiError(message, response.status);
  }

  return (response.status === 204 ? undefined : await response.json()) as T;
}

export function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Something went wrong.";
}
