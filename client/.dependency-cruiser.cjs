// SPDX-License-Identifier: AGPL-3.0-or-later
/**
 * Module-boundary rules for the client (checked in CI via `npm run lint:deps`):
 * the generated API client stays isolated, map and 3D scene modules stay UI-free,
 * the 3D engine library stays behind its one module, and page slices don't reach
 * into each other's internals.
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
      comment: 'Map and 3D scene modules must not depend on React pages/components (one-way flow).',
      severity: 'error',
      from: { path: '^src/(map|scene3d)/' },
      to: { path: '^src/(pages|components)/' },
    },
    {
      name: 'cesium-only-in-the-3d-scene-module',
      comment:
        'Only src/scene3d/scene3dContext.ts may import the 3D engine library; everything else '
        + 'goes through the Scene3DEngine contract, so the engine stays replaceable and its '
        + 'weight stays out of bundles that show no 3D. Type-only imports count. Test files are '
        + 'excluded from this graph, so a test may import the engine to stand in for it.',
      severity: 'error',
      from: { pathNot: '^src/scene3d/scene3dContext\\.ts$' },
      to: { path: '^node_modules/(cesium|@cesium)/' },
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
