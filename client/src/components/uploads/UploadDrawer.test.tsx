// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';

const { uploadOne, openBatch, closeBatch } = vi.hoisted(() => ({
  uploadOne: vi.fn(),
  openBatch: vi.fn(),
  closeBatch: vi.fn(),
}));

vi.mock('./uploadTransport.ts', async (importOriginal) => ({
  ...(await importOriginal<typeof import('./uploadTransport.ts')>()),
  uploadOne,
  abandonSession: vi.fn(() => Promise.resolve()),
}));

vi.mock('../../api/hooks.ts', () => ({
  useFileConfig: () => ({ data: undefined }),
  useOpenUploadBatch: () => ({ mutateAsync: openBatch }),
  useCloseUploadBatch: () => ({ mutateAsync: closeBatch }),
}));

const { default: UploadDrawer } = await import('./UploadDrawer.tsx');

/** Settle the batch-opening request the drawer is waiting on. */
let answerOpen: (batch: { id: string }) => void = () => undefined;
let refuseOpen: (error: unknown) => void = () => undefined;

beforeEach(async () => {
  await i18n.changeLanguage('en');
  uploadOne.mockReset();
  openBatch.mockReset();
  closeBatch.mockReset();
  // Held open until the test answers it, so every file of the drop arrives before the batch is
  // known — which is how a real drop meets the network.
  openBatch.mockImplementation(
    () =>
      new Promise((resolve, reject) => {
        answerOpen = resolve;
        refuseOpen = reject;
      }),
  );
  closeBatch.mockResolvedValue(undefined);
});

afterEach(cleanup);

function renderDrawer(onClose = vi.fn()) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <App>
        <UploadDrawer open onClose={onClose} cabinetId="c1" />
      </App>
    </QueryClientProvider>,
  );
  return onClose;
}

function drop(...names: string[]) {
  const input = screen.getByTestId('upload-files').querySelector('input[type="file"]')!;
  fireEvent.change(input, { target: { files: names.map((name) => new File([new Uint8Array(4)], name)) } });
}

describe('UploadDrawer', () => {
  it('opens one batch for a drop of several files, and files every one of them under it', async () => {
    uploadOne.mockResolvedValue({ fileId: 'f', documentId: 'd' });
    renderDrawer();

    drop('a.txt', 'b.txt', 'c.txt');
    // Every file has arrived and is waiting on the one opening request.
    await waitFor(() => expect(openBatch).toHaveBeenCalledTimes(1));
    await act(async () => answerOpen({ id: 'batch-1' }));

    await waitFor(() => expect(uploadOne).toHaveBeenCalledTimes(3));
    expect(openBatch).toHaveBeenCalledTimes(1);
    for (const call of uploadOne.mock.calls) {
      expect(call[1]).toMatchObject({ batchId: 'batch-1', cabinetId: 'c1' });
    }
  });

  it('asks before a close stops files that are still going, and stops them only when told to', async () => {
    const signals: AbortSignal[] = [];
    uploadOne.mockImplementation((_file: File, _target: unknown, _limits: unknown, options: { signal: AbortSignal }) => {
      signals.push(options.signal);
      return new Promise(() => undefined);
    });
    const onClose = renderDrawer();
    drop('a.txt', 'b.txt');
    await waitFor(() => expect(openBatch).toHaveBeenCalledTimes(1));
    await act(async () => answerOpen({ id: 'batch-1' }));
    await waitFor(() => expect(signals.length).toBeGreaterThan(0));

    // Escape is the habit that used to end a 200-file drop without a word.
    act(() => {
      fireEvent.keyDown(document.querySelector('.ant-drawer')!, { key: 'Escape' });
    });
    await waitFor(() => expect(document.querySelectorAll('.ant-modal-confirm')).toHaveLength(1));
    expect(document.querySelector('.ant-modal-confirm-title')).toHaveTextContent('Stop this upload?');
    expect(signals.some((signal) => signal.aborted)).toBe(false);

    fireEvent.click(screen.getByRole('button', { name: 'Keep uploading' }));
    expect(onClose).not.toHaveBeenCalled();
    expect(signals.some((signal) => signal.aborted)).toBe(false);

    fireEvent.click(screen.getByTestId('upload-stop'));
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Stop' }).length).toBeGreaterThan(0));
    // The newest confirmation is the one just asked; the earlier one may still be fading out.
    fireEvent.click(screen.getAllByRole('button', { name: 'Stop' }).at(-1)!);

    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
    expect(signals.every((signal) => signal.aborted)).toBe(true);
    expect(closeBatch).toHaveBeenCalledWith('batch-1');
  });

  it('names why the server refused a file, and offers to try the refused one again', async () => {
    uploadOne
      .mockResolvedValueOnce({ fileId: 'f', documentId: 'd' })
      .mockRejectedValueOnce(new ApiError(413, 'file.too_large', 'The file is larger than this installation accepts.'));
    renderDrawer();

    drop('a.txt', 'big.tif');
    await waitFor(() => expect(openBatch).toHaveBeenCalledTimes(1));
    await act(async () => answerOpen({ id: 'batch-1' }));

    expect(await screen.findByText(i18n.t('uploads.reason.tooLarge'))).toBeInTheDocument();
    expect(screen.getByRole('button', { name: i18n.t('uploads.retryFailed', { count: 1 }) })).toBeInTheDocument();
  });

  it('still uploads every file when the drop itself could not be opened, without a batch to file them under', async () => {
    uploadOne.mockResolvedValue({ fileId: 'f', documentId: 'd' });
    renderDrawer();

    drop('a.txt', 'b.txt');
    await waitFor(() => expect(openBatch).toHaveBeenCalledTimes(1));
    await act(async () => refuseOpen(new ApiError(403, 'access.forbidden', 'Forbidden.')));

    await waitFor(() => expect(uploadOne).toHaveBeenCalledTimes(2));
    for (const call of uploadOne.mock.calls) {
      expect(call[1]).toMatchObject({ batchId: undefined, cabinetId: 'c1' });
    }
  });
});
