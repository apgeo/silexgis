// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { formatSize } from './fileFormat.ts';

/**
 * One size, said one way, wherever a file is listed: the drawer, the document page and the cabinet
 * table used to give one small note as "1 KB" in two places and "39 B" in the third.
 */
describe('a file size in words', () => {
  it('gives a file under a kilobyte in bytes rather than rounding it up to one', () => {
    expect(formatSize(39)).toBe('39 B');
    expect(formatSize(0)).toBe('0 B');
  });

  it('keeps one decimal while the figure is small, and drops a trailing zero', () => {
    expect(formatSize(17_920)).toBe('17.5 KB');
    expect(formatSize(1024)).toBe('1 KB');
    expect(formatSize(1.25 * 1024 * 1024)).toBe('1.3 MB');
  });

  it('gives a large figure whole, and climbs to gigabytes', () => {
    expect(formatSize(300 * 1024)).toBe('300 KB');
    expect(formatSize(3 * 1024 * 1024 * 1024)).toBe('3 GB');
  });

  it("writes the figure in the page's language, so Romanian keeps its decimal comma", () => {
    expect(formatSize(17_920, 'ro')).toBe('17,5 KB');
    expect(formatSize(1.25 * 1024 * 1024, 'ro')).toBe('1,3 MB');
    expect(formatSize(17_920, 'en')).toBe('17.5 KB');
  });
});
