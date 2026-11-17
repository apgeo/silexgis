// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';

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

beforeEach(async () => {
  await i18n.changeLanguage('en');
  uploadOne.mockReset();
  openBatch.mockReset();
  closeBatch.mockReset();
  // Slow enough that every file of the drop arrives before the batch is known — which is how
  // a real drop meets the network.
  openBatch.mockImplementation(
    () => new Promise((resolve) => setTimeout(() => resolve({ id: 'batch-1' }), 20)),
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
});
