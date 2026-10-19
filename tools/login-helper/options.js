const api = globalThis.browser ?? globalThis.chrome;

const form = document.getElementById("form");
const input = document.getElementById("origin");
const status = document.getElementById("status");

function show(text, isError = false) {
  status.textContent = text;
  status.className = isError ? "error" : "";
}

function matchPattern(origin) {
  const url = new URL(origin);
  return `${url.protocol}//${url.hostname}/*`;
}

const DEFAULT_ORIGIN = "http://localhost:5299";

api.storage.local.get("origin").then(({ origin }) => {
  input.value = origin || DEFAULT_ORIGIN;
});

form.addEventListener("submit", (event) => {
  event.preventDefault();

  let origin;
  try {
    const url = new URL(input.value.trim());
    if (url.protocol !== "http:" && url.protocol !== "https:") {
      throw new Error();
    }
    origin = url.origin;
  } catch {
    show("Enter an http:// or https:// address, like http://openstar:5299.", true);
    return;
  }

  // permissions.request must run straight from the click, before any other await.
  // custlogin.gm.com is listed too because Firefox treats host permissions as opt-in.
  api.permissions
    .request({ origins: [matchPattern(origin), "*://custlogin.gm.com/*"] })
    .then(async (granted) => {
      if (!granted) {
        show("Access was not granted, so the helper can't reach the setup page.", true);
        return;
      }

      const { origin: previous } = await api.storage.local.get("origin");
      if (previous && matchPattern(previous) !== matchPattern(origin)) {
        await api.permissions.remove({ origins: [matchPattern(previous)] }).catch(() => {});
      }

      await api.storage.local.set({ origin });
      input.value = origin;
      const result = await api.runtime.sendMessage({ type: "sync" });
      if (result?.ok) {
        show(`Saved. Open or reload ${origin}/setup to use the helper.`);
      } else {
        show(`Saved, but the helper could not attach to the page: ${result?.error ?? "unknown error"}`, true);
      }
    })
    .catch((error) => show(String(error), true));
});
