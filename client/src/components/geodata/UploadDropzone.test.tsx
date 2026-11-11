// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { UploadProgress } from '../../api/uploadWithProgress.ts';
import UploadDropzone from './UploadDropzone.tsx';
import { uploadErrorText } from './uploadErrorText.ts';
import i18n from '../../i18n';

/** An upload the test drives by hand: progress, then the server's answer. */
function controlledUpload() {
  const calls: {
    file: File;
    progress: (p: UploadProgress) => void;
    resolve: (v: { name: string }) => void;
    reject: (e: unknown) => void;
  }[] = [];
  const upload = (file: File, progress: (p: UploadProgress) => void) =>
    new Promise<{ name: string }>((resolve, reject) => calls.push({ file, progress, resolve, reject }));
  return { calls, upload };
}

function drop(container: HTMLElement, ...files: File[]) {
  const input = container.querySelector('input[type=file]')!;
  fireEvent.change(input, { target: { files } });
}

const file = (name: string, size: number) => new File([new Uint8Array(size)], name);

afterEach(cleanup);

describe('UploadDropzone', () => {
  it('follows a file from the first byte to the server taking it', async () => {
    const { calls, upload } = controlledUpload();
    const { container } = render(
      <UploadDropzone accept=".pdf" prompt="Drop" hint="Hint" upload={upload} doneText={(n) => `${n} queued`} />,
    );
    drop(container, file('sheet.pdf', 2048));
    const row = await screen.findByTestId('upload-item');
    expect(row.dataset.state).toBe('uploading');

    act(() => calls[0]!.progress({ loaded: 1024, total: 2048 }));
    expect(row.textContent).toContain('Uploading — 1 KB of 2 KB');

    act(() => calls[0]!.progress({ loaded: 2048, total: 2048 }));
    expect(row.dataset.state).toBe('checking');
    expect(row.textContent).toContain('the server is checking the file');

    await act(async () => calls[0]!.resolve({ name: 'sheet' }));
    expect(row.dataset.state).toBe('done');
    expect(row.textContent).toContain('sheet queued');
  });

  it('keeps the reason a file was refused on screen until it is dismissed', async () => {
    const { calls, upload } = controlledUpload();
    const { container } = render(
      <UploadDropzone accept=".pdf" prompt="Drop" hint="Hint" upload={upload} doneText={(n) => n} />,
    );
    drop(container, file('notes.doc', 10));
    await screen.findByTestId('upload-item');
    await act(async () => calls[0]!.reject(new ApiError(400, 'georeferenced_map.format_unsupported')));

    const row = screen.getByTestId('upload-item');
    expect(row.dataset.state).toBe('failed');
    expect(row.textContent).toContain('This kind of file is not accepted here.');

    fireEvent.click(screen.getByRole('button', { name: 'Dismiss notes.doc' }));
    expect(screen.queryByTestId('upload-item')).toBeNull();
  });

  it('shows each of several dropped files on its own row, and clears the finished ones together', async () => {
    const { calls, upload } = controlledUpload();
    const { container } = render(
      <UploadDropzone accept=".pdf" prompt="Drop" hint="Hint" upload={upload} doneText={(n) => n} />,
    );
    drop(container, file('a.pdf', 10), file('b.pdf', 10), file('c.pdf', 10));
    await screen.findAllByTestId('upload-item');
    expect(screen.getAllByTestId('upload-item')).toHaveLength(3);

    await act(async () => {
      calls[0]!.resolve({ name: 'a' });
      calls[1]!.reject(new ApiError(0));
    });
    fireEvent.click(screen.getByRole('button', { name: 'Clear finished' }));
    const left = screen.getAllByTestId('upload-item');
    expect(left).toHaveLength(1);
    expect(left[0]!.dataset.name).toBe('c.pdf');
  });
});

describe('uploadErrorText', () => {
  const t = i18n.getFixedT('en');

  it('words a refusal the server names', () => {
    expect(uploadErrorText(new ApiError(400, 'geofile.file_empty'), t)).toBe('The file is empty.');
  });

  it('tells a lost connection, a proxy size limit and a server fault apart', () => {
    expect(uploadErrorText(new ApiError(0), t)).toMatch(/connection to the server was lost/);
    expect(uploadErrorText(new ApiError(413), t)).toBe('The file is larger than the server accepts.');
    expect(uploadErrorText(new ApiError(502), t)).toMatch(/error 502/);
  });

  it("falls back to the server's own words for a refusal it does not know", () => {
    expect(uploadErrorText(new ApiError(400, 'something.new', 'The page is upside down.'), t)).toBe(
      'The page is upside down.',
    );
  });

  it('says so when the upload was stopped', () => {
    expect(uploadErrorText(new DOMException('x', 'AbortError'), t)).toBe('The upload was stopped.');
  });
});
