/// <reference types="chrome" />

/**
 * The WebExtension API. Firefox exposes a promise-based `browser`; Chromium
 * exposes `chrome` (also promise-based for the calls used here).
 */
export const api: typeof chrome = (globalThis as unknown as { browser?: typeof chrome }).browser ?? chrome;

export const SOURCE = "openstar-login-helper";
export const PAGE_SOURCE = "openstar-setup";
export const REDIRECT_PREFIX = "msauth.com.gm.mychevrolet://";
export const SCRIPT_ID = "openstar-setup-page";

/** OpenStar's default local address; granted in the manifest so the helper works out of the box. */
export const DEFAULT_ORIGIN = "http://localhost:5299";

/** Host match pattern for an origin. Ports are dropped because Firefox rejects them; content.ts checks the exact origin. */
export function matchPattern(origin: string): string {
  const url = new URL(origin);
  
  return `${url.protocol}//${url.hostname}/*`;
}

/** What the content script sends to the setup page (without the source tag). */
export type HelperPayload =
  | { type: "ready"; version: string }
  | { type: "redirect"; id: string; redirect: string };

/** Messages the content script posts to the setup page. */
export type HelperMessage = { source: typeof SOURCE } & HelperPayload;

/** Messages the setup page posts back to the content script. */
export type PageMessage = { source: typeof PAGE_SOURCE } & (
  | { type: "ping" }
  | { type: "redirect-result"; id: string; accepted: boolean }
);
