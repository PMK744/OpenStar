#!/bin/bash
# Runs the API and web app in one container and gives both the same admin key,
# so the web UI needs no key entry (it injects the key when proxying the API).
set -euo pipefail

DATA="${OPENSTAR_DATA:-/data}"
mkdir -p "$DATA"

# The shared admin key: an explicit override wins, else reuse the persisted one,
# else generate a new one and persist it in the data volume.
if [[ -n "${OPENSTAR_ADMIN_KEY:-}" ]]; then
  KEY="$OPENSTAR_ADMIN_KEY"
elif [[ -f "$DATA/admin.key" ]]; then
  KEY="$(cat "$DATA/admin.key")"
else
  KEY="osa_$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')"
  printf '%s' "$KEY" > "$DATA/admin.key"
  chmod 600 "$DATA/admin.key"
fi

# Both apps read OpenStar:AdminKey; the API trusts it, the web injects it.
export OpenStar__AdminKey="$KEY"
export OpenStar__DataDirectory="$DATA"
export OpenStar__LoginRelayPort=0
export OpenStar__ApiBaseUrl="http://localhost:5290"

echo "OpenStar: admin key is managed automatically; the web UI at :5299 needs no key."
echo "OpenStar: direct API access at :5290 uses the key in $DATA/admin.key (or a client key)."

# Start an app from its own directory (so ASP.NET's content root — and thus
# wwwroot — resolves correctly), by its self-contained executable when present
# (CI image), else via the dotnet runtime (from-source image).
start() {
  local dir="$1" name="$2" url="$3"
  if [[ -x "/app/$dir/$name" ]]; then
    ( cd "/app/$dir" && exec "./$name" --urls "$url" ) &
  else
    ( cd "/app/$dir" && exec dotnet "./$name.dll" --urls "$url" ) &
  fi
}

start api OpenStar.Api "http://0.0.0.0:5290"
API_PID=$!
start web OpenStar.Web "http://0.0.0.0:5299"
WEB_PID=$!

shutdown() {
  kill "$API_PID" "$WEB_PID" 2>/dev/null || true
}
trap shutdown TERM INT

# If either process exits, stop the other so the container exits and restarts.
wait -n "$API_PID" "$WEB_PID"
shutdown
wait || true
