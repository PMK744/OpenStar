// Runs on the configured OpenStar origin. Bridges between the extension and the
// setup page with window.postMessage; it never reads anything else from the page.
import { api, PAGE_SOURCE, SOURCE, type HelperPayload, type PageMessage } from "./shared";

const ACCEPT_TIMEOUT_MS = 1500;

(() => {
  const marker = globalThis as unknown as { __openStarLoginHelper?: boolean };
  if (marker.__openStarLoginHelper) return;

  marker.__openStarLoginHelper = true;

  const post = (message: HelperPayload) => window.postMessage({ source: SOURCE, ...message }, location.origin);
  const announce = () => post({ type: "ready", version: api.runtime.getManifest().version });

  const pending = new Map<string, (accepted: boolean) => void>();

  window.addEventListener("message", (event: MessageEvent<PageMessage>) => {
    if (
      event.source !== window ||
      event.origin !== location.origin ||
      event.data?.source !== PAGE_SOURCE
    ) return;

    if (event.data.type === "ping") announce();
    else if (event.data.type === "redirect-result")
      pending.get(event.data.id)?.(event.data.accepted === true);
  });

  void api.storage.local.get("origin").then(({ origin }) => {
    if (location.origin !== origin) return;

    announce();

    api.runtime.onMessage.addListener((message, sender, sendResponse) => {
      if (
        sender.id !== api.runtime.id ||
        message?.type !== "redirect" ||
        typeof message.redirect !== "string"
      ) return false;

      const id = crypto.randomUUID();
      const finish = (accepted: boolean) => {
        pending.delete(id);
        clearTimeout(timer);
        sendResponse({ accepted });
      };

      const timer = setTimeout(() => finish(false), ACCEPT_TIMEOUT_MS);

      pending.set(id, finish);

      post({ type: "redirect", id, redirect: message.redirect });

      return true;
    });
  });
})();
