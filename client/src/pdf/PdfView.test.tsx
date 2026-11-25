// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import '../i18n';
import type { LoadedPdf, RenderedPdfPage } from './pdfEngine.ts';
import PdfView from './PdfView.tsx';

const engine = vi.hoisted(() => ({ loadPdf: vi.fn() }));
vi.mock('./pdfEngine.ts', () => engine);

interface FakeRender {
  page: number;
  width: number;
  signal?: AbortSignal;
  disposed: boolean;
  finish: () => void;
  fail: (error: Error) => void;
}

/**
 * A document whose page drawings finish when the test says so.
 *
 * Modelled on the rendering library in the one respect that matters here: a canvas takes one
 * drawing at a time — starting a second while the first is still running is refused with the
 * library's own complaint — and a drawing that is cancelled lets go of the canvas at once.
 */
function fakePdf(pageCount = 5) {
  const renders: FakeRender[] = [];
  const busy = new Set<HTMLCanvasElement>();

  const renderPage = vi.fn(
    ({ page, canvas, width, signal }: Parameters<LoadedPdf['renderPage']>[0] & { signal?: AbortSignal }) => {
      if (busy.has(canvas)) {
        return Promise.reject(new Error('Cannot use the same canvas during multiple render() operations'));
      }
      busy.add(canvas);

      return new Promise<RenderedPdfPage>((resolve, reject) => {
        let settled = false;
        const entry: FakeRender = {
          page,
          width,
          signal,
          disposed: false,
          finish: () => {
            if (settled) return;
            settled = true;
            busy.delete(canvas);
            resolve({ width, height: width * 1.4, dispose: () => { entry.disposed = true; } });
          },
          fail: (error) => {
            if (settled) return;
            settled = true;
            busy.delete(canvas);
            reject(error);
          },
        };
        signal?.addEventListener('abort', () => entry.fail(new Error('Rendering cancelled')), { once: true });
        renders.push(entry);
      });
    },
  );

  const pdf: LoadedPdf = {
    pageCount,
    renderPage,
    pageText: vi.fn(async () => ''),
    destroy: vi.fn(async () => {}),
  };
  return { pdf, renders, renderPage };
}

// The viewer draws only once it knows how wide its frame is, and the test DOM lays nothing out.
const clientWidth = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth');
beforeAll(() => {
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 600 });
});
afterAll(() => {
  if (clientWidth) {
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', clientWidth);
  }
});

afterEach(() => {
  cleanup();
  engine.loadPdf.mockReset();
});

const pdfFailed = /could not be laid out here/;
const pageFailed = /This page could not be drawn/;

describe('PdfView', () => {
  it('turns two pages quickly without failing, and draws the last page asked for', async () => {
    const { pdf, renders } = fakePdf();
    engine.loadPdf.mockResolvedValue(pdf);
    render(<PdfView url="/api/v1/files/f/content?token=x" />);

    await waitFor(() => expect(renders).toHaveLength(1));
    await act(async () => renders[0].finish());

    // Two presses inside the time one page takes to draw: the second must not start drawing
    // onto a canvas the first still holds.
    fireEvent.click(screen.getByLabelText('Next page'));
    await waitFor(() => expect(renders).toHaveLength(2));
    fireEvent.click(screen.getByLabelText('Next page'));
    await waitFor(() => expect(renders).toHaveLength(3));

    // The page nobody waits for any more was told to stop rather than left running.
    expect(renders[1].page).toBe(2);
    expect(renders[1].signal?.aborted).toBe(true);
    expect(renders[2].page).toBe(3);

    await act(async () => renders[2].finish());

    expect(screen.queryByText(pdfFailed)).toBeNull();
    expect(screen.queryByText(pageFailed)).toBeNull();
    expect(screen.getByText('Page 3 of 5')).toBeTruthy();
  });

  it('zooms in twice on one page without failing', async () => {
    const { pdf, renders } = fakePdf();
    engine.loadPdf.mockResolvedValue(pdf);
    render(<PdfView url="/api/v1/files/f/content?token=x" />);

    await waitFor(() => expect(renders).toHaveLength(1));
    await act(async () => renders[0].finish());

    fireEvent.click(screen.getByLabelText('Zoom in'));
    await waitFor(() => expect(renders).toHaveLength(2));
    fireEvent.click(screen.getByLabelText('Zoom in'));
    await waitFor(() => expect(renders).toHaveLength(3));
    await act(async () => renders[2].finish());

    expect(renders[2].width).toBeGreaterThan(renders[1].width);
    expect(screen.queryByText(pdfFailed)).toBeNull();
    expect(screen.queryByText(pageFailed)).toBeNull();
    // A page drawn and then replaced gives its text runs back.
    expect(renders[0].disposed).toBe(true);
  });

  it('says a page could not be drawn, keeps the reader in the document and draws it again on retry', async () => {
    const { pdf, renders } = fakePdf();
    engine.loadPdf.mockResolvedValue(pdf);
    render(<PdfView url="/api/v1/files/f/content?token=x" />);

    await waitFor(() => expect(renders).toHaveLength(1));
    await act(async () => renders[0].fail(new Error('bad page')));

    expect(await screen.findByText(pageFailed)).toBeTruthy();
    // Not the end of the document: the controls are still there to move past the page.
    expect(screen.getByLabelText('Next page')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(renders).toHaveLength(2));
    expect(renders[1].page).toBe(1);
    await act(async () => renders[1].finish());

    expect(screen.queryByText(pageFailed)).toBeNull();
  });

  it('says a document could not be loaded and loads it again on retry', async () => {
    const { pdf, renders } = fakePdf();
    engine.loadPdf.mockRejectedValueOnce(new Error('the document could not be fetched (503)'));
    engine.loadPdf.mockResolvedValueOnce(pdf);
    render(<PdfView url="/api/v1/files/f/content?token=x" />);

    expect(await screen.findByText(pdfFailed)).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(renders).toHaveLength(1));
    await act(async () => renders[0].finish());

    expect(engine.loadPdf).toHaveBeenCalledTimes(2);
    expect(screen.queryByText(pdfFailed)).toBeNull();
  });
});
