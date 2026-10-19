// Watches GM's sign-in for the final redirect to the myChevrolet app scheme and
// hands it to the OpenStar setup page. Chrome runs this as a service worker,
// Firefox as a background script.
import { api, DEFAULT_ORIGIN, matchPattern, REDIRECT_PREFIX, SCRIPT_ID } from "./shared";

async function ensureDefaultOrigin(): Promise<void> {
  const { origin } = await api.storage.local.get("origin");

  if (!origin) await api.storage.local.set({ origin: DEFAULT_ORIGIN });
}

async function configuredOrigin(): Promise<string | null> {
  const { origin } = await api.storage.local.get("origin");

  return typeof origin === "string" && origin ? origin : null;
}

async function setupTabs(origin: string): Promise<chrome.tabs.Tab[]> {
  const tabs = await api.tabs.query({ url: matchPattern(origin) });

  return tabs.filter((tab) => {
    try {
      return Boolean(tab.url && new URL(tab.url).origin === origin);
    } catch {
      return false;
    }
  });
}

/** Registers content.js on the configured OpenStar origin, if the user granted access to it. */
async function syncContentScript(): Promise<void> {
  const registered = await api.scripting.getRegisteredContentScripts({ ids: [SCRIPT_ID] });

  if (registered.length > 0) await api.scripting.unregisterContentScripts({ ids: [SCRIPT_ID] });

  const origin = await configuredOrigin();
  if (!origin || !(await api.permissions.contains({ origins: [matchPattern(origin)] }))) return;

  await api.scripting.registerContentScripts([
    { id: SCRIPT_ID, matches: [matchPattern(origin)], js: ["content.js"], runAt: "document_idle" },
  ]);

  // Setup pages that were already open get the script too; content.js ignores a second injection.
  for (const tab of await setupTabs(origin)) {
    if (tab.id !== undefined) {
      api.scripting.executeScript({ target: { tabId: tab.id }, files: ["content.js"] }).catch(() => { });
    }
  }
}

async function deliver(redirect: string, gmTabId: number): Promise<void> {
  const origin = await configuredOrigin();
  if (!origin) return;

  for (const tab of await setupTabs(origin)) {
    if (tab.id === undefined) continue;

    let response: { accepted?: boolean } | undefined;
    try {
      response = await api.tabs.sendMessage(tab.id, { type: "redirect", redirect });
    } catch {
      continue; // No content script in this tab (e.g. opened before access was granted).
    }

    if (response?.accepted) {
      await api.tabs.update(tab.id, { active: true }).catch(() => { });

      if (tab.windowId !== undefined) {
        await api.windows.update(tab.windowId, { focused: true }).catch(() => { });
      }

      if (gmTabId >= 0 && gmTabId !== tab.id) {
        await api.tabs.remove(gmTabId).catch(() => { });
      }

      return;
    }
  }
}

api.webRequest.onHeadersReceived.addListener(
  (details) => {
    const location = details.responseHeaders?.find((header) => header.name.toLowerCase() === "location")?.value;

    if (location && location.toLowerCase().startsWith(REDIRECT_PREFIX)) {
      deliver(location, details.tabId).catch((error) => console.error("OpenStar login helper:", error));
    }
  },
  { urls: ["*://custlogin.gm.com/*"] },
  ["responseHeaders"],
);

api.runtime.onInstalled.addListener(() => void ensureDefaultOrigin().then(syncContentScript));
api.runtime.onStartup.addListener(() => void ensureDefaultOrigin().then(syncContentScript));
api.permissions.onRemoved.addListener(() => void syncContentScript());

api.runtime.onMessage.addListener((message, sender, sendResponse) => {
  // Only the options page may reconfigure the extension.
  if (sender.id !== api.runtime.id || sender.tab || message?.type !== "sync") return false;
  
  syncContentScript().then(
    () => sendResponse({ ok: true }),
    (error: unknown) => sendResponse({ ok: false, error: String(error) }),
  );
  
  return true;
});
