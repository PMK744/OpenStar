# @pmkerby/utilities

Shared framework-agnostic TypeScript utilities for the OpenStar web application.

Utilities in this package should be small, deterministic, and independent of React, React Native, Vite, and Expo so they can be used from any workspace package.

```ts
import { isDefined } from "@pmkerby/utilities";

const values = [1, undefined, 2].filter(isDefined);
```

The package is source-first so Vite can transpile the shared TypeScript directly from the workspace.
