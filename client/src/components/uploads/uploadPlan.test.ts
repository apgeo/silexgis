// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  chunkRanges,
  planUpload,
  relativePathOf,
  resetForRetry,
  retryable,
  shouldResume,
  summarise,
  withItem,
  type UploadItem,
} from './uploadPlan.ts';

/** A file with the folder path a browser reports for a directory drop. */
function fileAt(path: string, size = 10): File {
  const name = path.split('/').pop()!;
  const file = new File([new Uint8Array(size)], name);
  Object.defineProperty(file, 'webkitRelativePath', { value: path === name ? '' : path });
  return file;
}

describe('relativePathOf', () => {
  it('reports the folder path for a dropped folder', () => {
    expect(relativePathOf(fileAt('1987/bulletins/march.pdf'))).toBe('1987/bulletins/march.pdf');
  });

  it('reports the bare name for a file chosen on its own', () => {
    // A mixed drop is the case that matters: files picked individually have no folder and
    // must not acquire one from the ones that do.
    expect(relativePathOf(fileAt('march.pdf'))).toBe('march.pdf');
  });
});

describe('planUpload', () => {
  it('keys rows positionally so two files of the same name stay separate', () => {
    // A folder drop routinely holds two files called the same thing in different folders,
    // and a list whose rows share a key updates the wrong row.
    const items = planUpload([fileAt('1987/scan.tif'), fileAt('1988/scan.tif')]);

    expect(items.map((i) => i.id)).toEqual(['u-0', 'u-1']);
    expect(items.map((i) => i.relativePath)).toEqual(['1987/scan.tif', '1988/scan.tif']);
    expect(items.every((i) => i.status === 'pending' && i.progress === 0)).toBe(true);
  });
});

describe('withItem', () => {
  it('changes one row and leaves the others identical', () => {
    const items = planUpload([fileAt('a.txt'), fileAt('b.txt')]);

    const next = withItem(items, 'u-1', { status: 'stored', progress: 1 });

    expect(next[1]).toMatchObject({ status: 'stored', progress: 1 });
    expect(next[0]).toBe(items[0]);
  });
});

describe('summarise', () => {
  const items: UploadItem[] = [
    { id: 'a', file: fileAt('a'), relativePath: 'a', status: 'stored', progress: 1 },
    { id: 'b', file: fileAt('b'), relativePath: 'b', status: 'skipped', progress: 1 },
    { id: 'c', file: fileAt('c'), relativePath: 'c', status: 'failed', progress: 0 },
    { id: 'd', file: fileAt('d'), relativePath: 'd', status: 'pending', progress: 0 },
  ];

  it('counts each outcome', () => {
    expect(summarise(items)).toMatchObject({ total: 4, stored: 1, skipped: 1, failed: 1, pending: 1 });
  });

  it('is still running while anything is queued or in flight', () => {
    expect(summarise(items).running).toBe(true);
    expect(summarise(items.slice(0, 3)).running).toBe(false);

    const inFlight: UploadItem[] = [
      { id: 'e', file: fileAt('e'), relativePath: 'e', status: 'uploading', progress: 0.4 },
    ];
    expect(summarise(inFlight).running).toBe(true);
  });

  it('calls an empty drop finished rather than running', () => {
    expect(summarise([])).toMatchObject({ total: 0, running: false });
  });
});

describe('retrying', () => {
  const items: UploadItem[] = [
    { id: 'a', file: fileAt('a'), relativePath: 'a', status: 'stored', progress: 1 },
    { id: 'b', file: fileAt('b'), relativePath: 'b', status: 'failed', progress: 0, errorCode: 'x' },
    { id: 'c', file: fileAt('c'), relativePath: 'c', status: 'skipped', progress: 1 },
  ];

  it('offers only what failed', () => {
    // Re-sending four hundred files because twelve failed is both slow and, for the ones that
    // worked, refused as duplicates.
    expect(retryable(items).map((i) => i.id)).toEqual(['b']);
  });

  it('requeues the failures and leaves everything else alone', () => {
    const next = resetForRetry(items);

    expect(next[1]).toMatchObject({ status: 'pending', progress: 0, errorCode: undefined });
    expect(next[0].status).toBe('stored');
    expect(next[2].status).toBe('skipped');
  });
});

describe('shouldResume', () => {
  it('is for large files only', () => {
    expect(shouldResume(40_000_000, 32 * 1024 * 1024)).toBe(true);
    expect(shouldResume(4_000_000, 32 * 1024 * 1024)).toBe(false);
  });

  it('is off when the server publishes no threshold', () => {
    expect(shouldResume(999_999_999, 0)).toBe(false);
  });
});

describe('chunkRanges', () => {
  it('covers the file exactly', () => {
    expect(chunkRanges(25, 10)).toEqual([
      { start: 0, end: 10 },
      { start: 10, end: 20 },
      { start: 20, end: 25 },
    ]);
  });

  it('starts from what the server already holds', () => {
    // The whole of resuming: a client that lost its connection sends the remainder, not the
    // file.
    expect(chunkRanges(25, 10, 10)).toEqual([
      { start: 10, end: 20 },
      { start: 20, end: 25 },
    ]);
  });

  it('has nothing left to send once everything has landed', () => {
    expect(chunkRanges(25, 10, 25)).toEqual([]);
    expect(chunkRanges(0, 10)).toEqual([]);
  });
});
