// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../api/client.ts';
import type { UploadItem } from './uploadPlan.ts';
import { useUploadQueue } from './useUploadQueue.ts';

const uploadOne = vi.hoisted(() => vi.fn());

vi.mock('./uploadTransport.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./uploadTransport.ts')>()),
  uploadOne,
}));

const limits = { chunkBytes: 1024, resumableThresholdBytes: 0 };

function files(...names: string[]): File[] {
  return names.map((name) => new File([new Uint8Array(4)], name));
}

beforeEach(() => {
  uploadOne.mockReset();
  uploadOne.mockResolvedValue({ fileId: 'f', documentId: 'd' });
});

describe('useUploadQueue', () => {
  it('uploads every file of a multi-file drop', async () => {
    // The defect this pins: a drop of three files that sent one, because the control that
    // announced them was disabled while the first was in flight.
    const { result } = renderHook(() => useUploadQueue(limits, () => ({})));

    act(() => result.current.add(files('a.txt', 'b.txt', 'c.txt')));

    await waitFor(() => expect(result.current.summary.running).toBe(false));
    expect(result.current.summary.stored).toBe(3);
    expect(uploadOne).toHaveBeenCalledTimes(3);
  });

  it('carries on past a failure and records which file it was', async () => {
    // An import that stops dead on the first unreadable scan has to be restarted by hand,
    // and restarting it re-sends everything that already worked.
    uploadOne.mockImplementation((file: File) =>
      file.name === 'b.txt'
        ? Promise.reject(new ApiError(400, 'file.type_not_accepted'))
        : Promise.resolve({ fileId: 'f', documentId: 'd' }),
    );

    const { result } = renderHook(() => useUploadQueue(limits, () => ({})));
    act(() => result.current.add(files('a.txt', 'b.txt', 'c.txt')));

    await waitFor(() => expect(result.current.summary.running).toBe(false));
    expect(result.current.summary).toMatchObject({ stored: 2, failed: 1 });

    const failed = result.current.items.find((i: UploadItem) => i.status === 'failed');
    expect(failed?.relativePath).toBe('b.txt');
    expect(failed?.errorCode).toBe('file.type_not_accepted');
  });

  it('retries only what failed', async () => {
    uploadOne.mockImplementationOnce(() => Promise.reject(new ApiError(500)));

    const { result } = renderHook(() => useUploadQueue(limits, () => ({})));
    act(() => result.current.add(files('a.txt', 'b.txt')));
    await waitFor(() => expect(result.current.summary.running).toBe(false));
    expect(result.current.summary.failed).toBe(1);

    uploadOne.mockClear();
    act(() => result.current.retryFailed());
    await waitFor(() => expect(result.current.summary.running).toBe(false));

    // Re-sending the whole drop would be slow and, for the file that already landed,
    // refused as a duplicate.
    expect(uploadOne).toHaveBeenCalledTimes(1);
    expect(result.current.summary).toMatchObject({ stored: 2, failed: 0 });
  });

  it('records a declined duplicate as skipped rather than failed', async () => {
    const { DuplicateUploadError } = await import('./uploadTransport.ts');
    uploadOne.mockRejectedValue(new DuplicateUploadError());

    const { result } = renderHook(() => useUploadQueue(limits, () => ({})));
    act(() => result.current.add(files('a.txt')));

    await waitFor(() => expect(result.current.summary.running).toBe(false));

    // They were asked and said no, so offering to retry would ask the same question again.
    expect(result.current.summary).toMatchObject({ skipped: 1, failed: 0 });
  });

  it('sends each file to the destination its own path names', async () => {
    // What makes a folder drop a folder drop: the target is computed per file, from the path
    // the browser reported for it.
    const dropped = files('march.pdf');
    Object.defineProperty(dropped[0], 'webkitRelativePath', { value: '1987/bulletins/march.pdf' });

    const { result } = renderHook(() =>
      useUploadQueue(limits, (item) => ({ cabinetId: 'shelf', relativePath: item.relativePath })),
    );
    act(() => result.current.add(dropped));

    await waitFor(() => expect(result.current.summary.running).toBe(false));
    expect(uploadOne).toHaveBeenCalledWith(
      dropped[0],
      { cabinetId: 'shelf', relativePath: '1987/bulletins/march.pdf' },
      limits,
      expect.anything(),
    );
  });

  it('takes files added while it is already running', async () => {
    const { result } = renderHook(() => useUploadQueue(limits, () => ({})));

    act(() => result.current.add(files('a.txt')));
    act(() => result.current.add(files('b.txt')));

    await waitFor(() => expect(result.current.summary.running).toBe(false));

    // Ids continue across drops, so a second folder dropped onto a running list cannot
    // collide with rows already in it.
    expect(result.current.summary.stored).toBe(2);
    expect(new Set(result.current.items.map((i: UploadItem) => i.id)).size).toBe(2);
  });

  it('empties the list and stops what is in flight', async () => {
    const { result } = renderHook(() => useUploadQueue(limits, () => ({})));
    act(() => result.current.add(files('a.txt')));
    await waitFor(() => expect(result.current.summary.running).toBe(false));

    act(() => result.current.clear());
    expect(result.current.items).toEqual([]);
    expect(result.current.summary).toMatchObject({ total: 0, running: false });
  });
});
