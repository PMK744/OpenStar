# syntax=docker/dockerfile:1

# ── Build stage: front-end + extension (Bun) and the .NET apps ────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Bun builds the React front-end (wwwroot) and the login-helper extension.
COPY --from=oven/bun:1.2 /usr/local/bin/bun /usr/local/bin/bun

WORKDIR /src
COPY . .

RUN bun install
RUN bun run build

RUN dotnet publish applications/api/OpenStar.Api.csproj -c Release -o /out/api --nologo
RUN dotnet publish applications/web/OpenStar.Web.csproj -c Release -o /out/web --nologo

# Bundle the built extension so the web app's /login-helper.zip download works.
RUN mkdir -p /out/web/tools/login-helper \
    && cp -r tools/login-helper/dist /out/web/tools/login-helper/dist

# ── Runtime stage: both apps in one image ─────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Run as root so the mounted /data volume is always writable (these .NET images
# can default to a non-root user, which can't write a fresh named volume).
USER root

WORKDIR /app
COPY --from=build /out/api ./api
COPY --from=build /out/web ./web
COPY docker/entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh

# Encrypted state, data-protection keys, and the shared admin key live here.
ENV OPENSTAR_DATA=/data
VOLUME ["/data"]

# 5299 = web UI, 5290 = API (for external clients / your own apps).
EXPOSE 5299 5290

ENTRYPOINT ["./entrypoint.sh"]
