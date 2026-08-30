// SPDX-License-Identifier: AGPL-3.0-or-later
/**
 * Module-boundary rules for the client (checked in CI via `npm run lint:deps`):
 * the generated API client stays isolated, map and 3D scene modules stay UI-free,
 * the 3D engine, PDF and charting libraries each stay behind their one module, and page slices
 * don't reach into each other's internals.
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
      name: 'filter-logic-is-ui-free',
      comment:
        'The filter and selector logic must be usable without the control that draws it: another '
        + 'component, a table, an export dialog or a test may build, serialise and read a filter '
        + 'without mounting anything. Enforced rather than asserted, because "decoupled" is true '
        + 'the day it is written and quietly false six months later — one convenience import of a '
        + 'component is all it takes.',
      severity: 'error',
      from: { path: '^src/filters/' },
      to: { path: '^src/(pages|components)/' },
    },
    {
      name: 'filter-logic-imports-no-view-library',
      comment:
        'The same boundary from the other side: the logic may not reach for React or the component '
        + 'library at all, so it cannot grow a hook or a rendered default that only works inside a '
        + 'tree. Type-only imports count.',
      severity: 'error',
      from: { path: '^src/filters/' },
      to: { path: '^node_modules/(react|react-dom|antd|@ant-design)/' },
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
      name: 'pdfjs-only-in-the-pdf-module',
      comment:
        'Only src/pdf/pdfEngine.ts may import the PDF rendering library; everything else goes '
        + 'through PdfView and the small contract beside it, so the library stays replaceable '
        + 'and its weight stays out of bundles that show no PDF. Type-only imports count, and '
        + 'so does its stylesheet. Test files are excluded from this graph, so a test may '
        + 'import the library to stand in for it.',
      severity: 'error',
      from: { pathNot: '^src/pdf/pdfEngine\\.ts$' },
      to: { path: '^node_modules/pdfjs-dist/' },
    },
    {
      name: 'echarts-only-in-the-statistics-components',
      comment:
        'Only the chart components under src/components/statistics/ may import the charting '
        + 'library. It is roughly 190 kB gzipped, and it is paid for by whoever downloads the '
        + 'chunk it lands in: kept behind this one directory it rides in a lazily-loaded '
        + 'route\'s own chunk, and a single import from an eagerly-loaded module would move all '
        + 'of it into the entry bundle every signed-in reader fetches. Keeping the seam in one '
        + 'place also keeps the library replaceable. Type-only imports count, and so does its '
        + 'renderer package. Test files are excluded from this graph, so a test may import the '
        + 'library to stand in for it.',
      severity: 'error',
      from: { pathNot: '^src/components/statistics/' },
      to: { path: '^node_modules/(echarts|zrender)/' },
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
