// SPDX-License-Identifier: AGPL-3.0-or-later
/**
 * Module-boundary rules for the client (checked in CI via `npm run lint:deps`):
 * the generated API client stays isolated, map modules stay UI-free, and page
 * slices don't reach into each other's internals.
 */
module.exports = {
  forbidden: [
    {
      name: 'no-circular',
      severity: 'error',
      from: {},
      to: { circular: true },
    },
    {
      name: 'generated-schema-only-via-api',
      comment: 'The generated OpenAPI schema is consumed only by the api/ wrapper modules.',
      severity: 'error',
      from: { pathNot: '^src/api/' },
      to: { path: '^src/api/schema\\.d\\.ts$' },
    },
    {
      name: 'map-modules-are-ui-free',
      comment: 'OL map modules must not depend on React pages/components (one-way flow).',
      severity: 'error',
      from: { path: '^src/map/' },
      to: { path: '^src/(pages|components)/' },
    },
    {
      name: 'page-slices-stay-isolated',
      comment: 'A page slice may not import another page slice\'s internals.',
      severity: 'error',
      from: { path: '^src/pages/([^/]+)/' },
      to: { path: '^src/pages/', pathNot: ['^src/pages/$1/', '^src/pages/[^/]+\\.(tsx|ts|css)$'] },
    },
  ],
  options: {
    doNotFollow: { path: 'node_modules' },
    tsPreCompilationDeps: true,
    tsConfig: { fileName: 'tsconfig.app.json' },
    exclude: { path: '\\.(test|spec)\\.tsx?$' },
  },
};
