import axios, { AxiosError, type AxiosInstance } from "axios";

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

export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
  }
}

// The admin key the request interceptor attaches. In managed mode it stays null
// and the server-side proxy supplies the key instead.
let adminKey: string | null = null;

export function setAdminKey(key: string | null) {
  adminKey = key && key.length > 0 ? key : null;
}

/** Shared axios instance for the OpenStar API, with auth and error middleware. */
export const http: AxiosInstance = axios.create({ baseURL: apiBaseUrl });

// Request middleware: attach the admin key when we have one.
http.interceptors.request.use((config) => {
  if (adminKey) {
    config.headers.set("X-Api-Key", adminKey);
  }
  return config;
});

// Response middleware: turn axios/HTTP failures into a tidy ApiError.
http.interceptors.response.use(
  (response) => response,
  (error: AxiosError<{ title?: string; detail?: string }>) => {
    if (error.response) {
      const { status, data } = error.response;
      let message = `Request failed (${status}).`;
      if (data && typeof data === "object") {
        message = [data.title, data.detail].filter(Boolean).join(": ") || message;
      }
      if (status === 401) {
        message = "The admin key was rejected.";
      }
      return Promise.reject(new ApiError(message, status));
    }
    // No response = network/CORS failure (or the app isn't running).
    return Promise.reject(new ApiError("Could not reach the OpenStar app. Is it running?", 0));
  },
);

export async function apiRequest<T>(path: string, init: { method?: string; body?: unknown } = {}): Promise<T> {
  const response = await http.request<T>({
    url: path,
    method: init.method ?? "GET",
    data: init.body,
  });
  return response.data;
}

export function getStoredAdminKey(): string | null {
  try {
    return window.localStorage.getItem(adminKeyStorageKey);
  } catch {
    return null;
  }
}

export function storeAdminKey(key: string | null) {
  try {
    if (key) {
      window.localStorage.setItem(adminKeyStorageKey, key);
    } else {
      window.localStorage.removeItem(adminKeyStorageKey);
    }
  } catch {
    // Storage may be unavailable (private mode); the key then lasts for this page only.
  }
}

export function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Something went wrong.";
}
