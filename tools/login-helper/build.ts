// Transpiles the TypeScript sources into the loadable extension under dist/:
// each entry becomes a self-contained classic script, then the static files
// (manifest, options page) are copied alongside them.
import { cp, mkdir, rm } from "node:fs/promises";

const outdir = "dist";

await rm(outdir, { recursive: true, force: true });
await mkdir(outdir, { recursive: true });

const result = await Bun.build({
  entrypoints: ["src/background.ts", "src/content.ts", "src/options.ts"],
  outdir,
  target: "browser",
  // Classic scripts (not ES modules): content scripts and MV3 service workers
  // load as plain scripts, and each entry inlines the shared module.
  format: "iife",
  naming: "[name].js",
});

if (!result.success) {
  for (const log of result.logs) console.error(log);

  process.exit(1);
}

await cp("public", outdir, { recursive: true });
console.log(`Built the login helper extension to ${outdir}/`);
