// SPDX-License-Identifier: AGPL-3.0-or-later
import { readdirSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { bundledFeatureSymbols } from './featureSymbols.ts';

describe('bundledFeatureSymbols', () => {
  it('offers exactly the symbol files bundled beside the application', () => {
    const folder = path.resolve(process.cwd(), 'public/feature_symbols');
    const files = readdirSync(folder).filter((name) => /\.(png|svg)$/.test(name)).sort();

    expect([...bundledFeatureSymbols].sort()).toEqual(files);
  });

  it('names each file the way the server accepts a symbol', () => {
    for (const name of bundledFeatureSymbols) {
      expect(name).toMatch(/^[a-z0-9_-]+\.(png|svg)$/);
    }
  });
});
