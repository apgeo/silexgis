// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import {
  formatBytes,
  insufficientFigures,
  kindKey,
  levelAlertType,
  limitKey,
  originalBytesOf,
  percentOfCap,
  RECLAIM_GROUPS,
  reclaimGroupKeys,
  runAddsBytes,
  STORAGE_LIMITS,
  TILE_FACTOR_WITHOUT_ESTIMATE,
} from './storageFigures.ts';

function lookup(bundle: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], bundle);
}

describe('formatBytes', () => {
  it('says gigabytes as gigabytes', () => {
    expect(formatBytes(9 * 1024 ** 3)).toBe('9.0 GB');
    expect(formatBytes(1.5 * 1024 ** 2)).toBe('1.5 MB');
    expect(formatBytes(2048)).toBe('2 KB');
    expect(formatBytes(10)).toBe('10 B');
    expect(formatBytes(0)).toBe('0 B');
  });
});

describe('percentOfCap', () => {
  it('is null without a cap and may pass a hundred', () => {
    expect(percentOfCap(50, null)).toBeNull();
    expect(percentOfCap(50, 0)).toBeNull();
    expect(percentOfCap(75, 100)).toBe(75);
    expect(percentOfCap(120, 100)).toBe(120);
  });
});

describe('levelAlertType', () => {
  it('shows nothing for a normal store and escalates with the level', () => {
    expect(levelAlertType('normal')).toBeNull();
    expect(levelAlertType('notice')).toBe('info');
    expect(levelAlertType('warning')).toBe('warning');
    expect(levelAlertType('full')).toBe('error');
  });
});

describe('runAddsBytes', () => {
  it('counts the thinned copy at the size of the cloud beside the tiles, as the server does', () => {
    // Thinning then tiling: the copy and the tiles both stay.
    expect(runAddsBytes(true, 1000, 300)).toBe(1300);
    // The whole cloud tiled: the tiles alone.
    expect(runAddsBytes(false, 1000, 300)).toBe(300);
    // No estimate for the row: the measured upper factor over what is tiled.
    expect(runAddsBytes(false, 1000, null)).toBe(1000 * TILE_FACTOR_WITHOUT_ESTIMATE);
    expect(runAddsBytes(true, 1000, undefined)).toBe(1000 + 1000 * TILE_FACTOR_WITHOUT_ESTIMATE);
    // Nothing to go by.
    expect(runAddsBytes(false, null, null)).toBeNull();
  });

  it('takes the original from the archive file, as received or else as declared', () => {
    expect(originalBytesOf([{ role: 'archive', receivedSize: 900, declaredSize: 1000 }])).toBe(900);
    expect(originalBytesOf([{ role: 'archive', receivedSize: null, declaredSize: 1000 }])).toBe(1000);
    expect(originalBytesOf([{ role: 'config', receivedSize: 5, declaredSize: 5 }])).toBeNull();
  });
});

describe('insufficientFigures', () => {
  it('reads the missing amount from the refusal members, not the sentence', () => {
    expect(
      insufficientFigures({ code: 'storage.insufficient', limit: 'scans', missingBytes: 1024, detail: 'x' }),
    ).toEqual({ limit: 'scans', missingBytes: 1024 });
    expect(insufficientFigures({ code: 'file.quota_exceeded', missingBytes: 1 })).toBeNull();
    expect(insufficientFigures(undefined)).toBeNull();
  });
});

// The keys are built from values, which hides them from the scan for untranslated keys.
describe('storage wording', () => {
  const keys = [
    ...STORAGE_LIMITS.map(limitKey),
    limitKey('somethingNew'),
    ...[...RECLAIM_GROUPS, 'somethingNew'].flatMap((g) => Object.values(reclaimGroupKeys(g))),
    ...['notice', 'warning', 'full'].map((level) => `fileStorage.levels.${level}`),
    // What the server spells today: the file kinds, and the point-cloud kinds plus uploaded originals.
    ...['image', 'document', 'survey', 'raster', 'vector', 'model', 'other', 'audio', 'video'].map(kindKey),
    ...['original', 'point-cloud-laz', 'point-cloud-laz-display', 'point-cloud-tiles', 'trajectory', 'report'].map(kindKey),
  ];
  it.each(keys)('%s is in both locales', (key) => {
    expect(typeof lookup(en, key)).toBe('string');
    expect(typeof lookup(ro, key)).toBe('string');
  });
});
