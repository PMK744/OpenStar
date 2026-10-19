// Options page: stores the OpenStar web address and requests access to it so the
// helper can bridge the GM redirect to the setup page.
import { api, DEFAULT_ORIGIN, matchPattern } from "./shared";

const form = document.getElementById("form") as HTMLFormElement;
const input = document.getElementById("origin") as HTMLInputElement;
const status = document.getElementById("status") as HTMLParagraphElement;

function show(text: string, isError = false): void {
  status.textContent = text;
  status.className = isError ? "error" : "";
}

void api.storage.local.get("origin").then(({ origin }) => {
  input.value = (typeof origin === "string" && origin) || DEFAULT_ORIGIN;
});

form.addEventListener("submit", (event) => {
  event.preventDefault();

  let origin: string;
  try {
    const url = new URL(input.value.trim());

    if (url.protocol !== "http:" && url.protocol !== "https:") {
      throw new Error("Unsupported protocol");
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
      if (typeof previous === "string" && previous && matchPattern(previous) !== matchPattern(origin)) {
        await api.permissions.remove({ origins: [matchPattern(previous)] }).catch(() => {});
      }

      await api.storage.local.set({ origin });
      input.value = origin;

      const result = (await api.runtime.sendMessage({ type: "sync" })) as { ok?: boolean; error?: string } | undefined;
      if (result?.ok) {
        show(`Saved. Open or reload ${origin}/setup to use the helper.`);
      } else {
        show(`Saved, but the helper could not attach to the page: ${result?.error ?? "unknown error"}`, true);
      }
    })
    .catch((error: unknown) => show(String(error), true));
});
