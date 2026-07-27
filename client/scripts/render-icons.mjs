// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Renders the installable-app icon set from public/favicon.svg.
//
//   node scripts/render-icons.mjs
//
// Run by hand, not by the build: the PNGs are checked in, so installing SilexGIS needs no
// image toolchain. Re-run it after changing the logo — which is the only reason it exists,
// since a rebranded fork would otherwise have to reproduce these sizes by guesswork. It
// rasterises through the browser Playwright already installs for the tests, so it adds
// nothing to the dependency set.
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const publicDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'public');
const iconsDir = join(publicDir, 'icons');

// `scale` is the logo's width as a fraction of the canvas.
//
// A maskable icon is cropped to whatever shape the launcher fancies — a circle, a squircle,
// a rounded square — and only the centred 80% of it is guaranteed to survive. So those are
// drawn small on a filled background, with room to lose. The plain icons are composited by
// the browser as-is and keep their transparency; Apple's does not get that courtesy, since
// iOS flattens transparency onto black.
const targets = [
  { file: 'icon-192.png', size: 192, scale: 0.86, background: null },
  { file: 'icon-512.png', size: 512, scale: 0.86, background: null },
  { file: 'icon-192-maskable.png', size: 192, scale: 0.58, background: '#ffffff' },
  { file: 'icon-512-maskable.png', size: 512, scale: 0.58, background: '#ffffff' },
  { file: 'apple-touch-icon.png', size: 180, scale: 0.72, background: '#ffffff' },
];

const svg = await readFile(join(publicDir, 'favicon.svg'), 'utf8');
await mkdir(iconsDir, { recursive: true });

const browser = await chromium.launch();
try {
  for (const { file, size, scale, background } of targets) {
    const page = await browser.newPage({ viewport: { width: size, height: size } });
    // The source is 48x46, so height follows the aspect ratio and the flexbox centres it.
    await page.setContent(
      `<!doctype html><style>
         html, body { margin: 0; width: ${size}px; height: ${size}px; }
         body { display: flex; align-items: center; justify-content: center;
                background: ${background ?? 'transparent'}; }
         svg { width: ${Math.round(size * scale)}px; height: auto; }
       </style>${svg}`,
    );
    await writeFile(join(iconsDir, file), await page.screenshot({ omitBackground: background === null }));
    await page.close();
    console.log(`rendered ${file} (${size}x${size})`);
  }
} finally {
  await browser.close();
}
