// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import { importWithRetry, isCodeDownloadFailure } from './codeLoading.ts';

// The three browsers' words for a module that did not arrive, as each says it.
const CHROMIUM = new TypeError('Failed to fetch dynamically imported module: https://example.test/assets/a-1.js');
const FIREFOX = new TypeError('error loading dynamically imported module: https://example.test/assets/a-1.js');
const SAFARI = new TypeError('Importing a module script failed.');
const CSS = new Error('Unable to preload CSS for /assets/a-1.css');

const noWait = () => Promise.resolve();

describe('isCodeDownloadFailure', () => {
  it('knows each browser’s words for code that did not arrive, as an error or as its message', () => {
    for (const error of [CHROMIUM, FIREFOX, SAFARI, CSS]) {
      expect(isCodeDownloadFailure(error)).toBe(true);
      expect(isCodeDownloadFailure(error.message)).toBe(true);
    }
  });

  it('does not take a fault of the code itself for one', () => {
    expect(isCodeDownloadFailure(new Error('Cannot read properties of undefined'))).toBe(false);
    expect(isCodeDownloadFailure(undefined)).toBe(false);
    expect(isCodeDownloadFailure({ message: 'dynamically imported module' })).toBe(false);
  });
});

describe('importWithRetry', () => {
  it('asks again after a failed download, pausing as long as it says, and answers with what arrives', async () => {
    const load = vi
      .fn<() => Promise<string>>()
      .mockRejectedValueOnce(FIREFOX)
      .mockRejectedValueOnce(CHROMIUM)
      .mockResolvedValueOnce('the code');
    const waits: number[] = [];

    await expect(
      importWithRetry(load, [10, 20], (ms) => {
        waits.push(ms);
        return Promise.resolve();
      }),
    ).resolves.toBe('the code');
    expect(load).toHaveBeenCalledTimes(3);
    expect(waits).toEqual([10, 20]);
  });

  it('gives up with the last failure once every pause is spent', async () => {
    const load = vi.fn<() => Promise<string>>().mockRejectedValue(SAFARI);
    await expect(importWithRetry(load, [1, 1], noWait)).rejects.toBe(SAFARI);
    expect(load).toHaveBeenCalledTimes(3);
  });

  it('does not ask again when the code arrived and failed by itself', async () => {
    const broken = new Error('the module threw while it started');
    const load = vi.fn<() => Promise<string>>().mockRejectedValue(broken);
    await expect(importWithRetry(load, [1, 1], noWait)).rejects.toBe(broken);
    expect(load).toHaveBeenCalledTimes(1);
  });

  it('asks only once when it succeeds at once', async () => {
    const load = vi.fn<() => Promise<number>>().mockResolvedValue(7);
    await expect(importWithRetry(load)).resolves.toBe(7);
    expect(load).toHaveBeenCalledTimes(1);
  });
});
