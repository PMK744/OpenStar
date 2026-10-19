import { useEffect, useRef, useState } from "react";

// Message protocol shared with tools/login-helper/content.js.
const helperSource = "openstar-login-helper";
const pageSource = "openstar-setup";

/**
 * Detects the optional OpenStar login helper extension and receives the GM
 * redirect it captures. `onRedirect` returns whether the page used it, so the
 * extension knows whether to close the GM tab.
 */
export function useLoginHelper(onRedirect: (redirect: string) => boolean): boolean {
  const [isDetected, setIsDetected] = useState(false);
  const onRedirectRef = useRef(onRedirect);
  onRedirectRef.current = onRedirect;

  useEffect(() => {
    const post = (message: Record<string, unknown>) =>
      window.postMessage({ source: pageSource, ...message }, window.location.origin);

    const listener = (event: MessageEvent) => {
      if (event.source !== window || event.origin !== window.location.origin || event.data?.source !== helperSource) {
        return;
      }
      if (event.data.type === "ready") {
        setIsDetected(true);
      } else if (event.data.type === "redirect" && typeof event.data.redirect === "string") {
        setIsDetected(true);
        post({ type: "redirect-result", id: event.data.id, accepted: onRedirectRef.current(event.data.redirect) });
      }
    };

    window.addEventListener("message", listener);
    post({ type: "ping" });
    return () => window.removeEventListener("message", listener);
  }, []);

  return isDetected;
}
