// SPDX-License-Identifier: AGPL-3.0-or-later
import * as pdfjs from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import './textLayer.css';

/**
 * The one module that touches the PDF rendering library.
 *
 * Everything else in the application works against the small contract below, so the library
 * stays replaceable, its weight stays out of every bundle that shows no PDF, and there is a
 * single place where the two things that make it work offline are set: the worker it runs its
 * parser in, and the character-map and font tables it needs for documents that do not carry
 * their own. All three are served from this installation — an installation that reaches a
 * vendor's CDN to draw a page is one that shows nothing on a network that cannot, and quietly
 * tells that vendor which documents are being read.
 *
 * A boundary rule in the dependency configuration keeps this the only importer; type-only
 * imports count, so nothing can reach past it "just for a type".
 */

// Bundled as an asset of this build rather than fetched: the parser worker is the one file
// without which nothing renders at all.
pdfjs.GlobalWorkerOptions.workerSrc = workerUrl;

// Copied out of the installed package at build time and served from a versioned path, so a
// library upgrade moves every one of them and no browser can serve a cached table from one
// version to parser code from another. Declared by the build; read here.
declare const PDFJS_CMAP_URL: string;
declare const PDFJS_STANDARD_FONT_URL: string;
declare const PDFJS_WASM_URL: string;

/** A page drawn to a canvas, with the text runs that sit over it. */
export interface RenderedPdfPage {
  /** Natural size of the drawing at the scale it was asked for, in CSS pixels. */
  width: number;
  height: number;
  /** Frees the render task and the text runs; safe to call more than once. */
  dispose: () => void;
}

/** What a loaded document offers. One instance owns one parsed document. */
export interface LoadedPdf {
  pageCount: number;
  /**
   * Draws one page into `canvas` and lays its text runs out inside `textLayer`, positioned so
   * a browser selection over them lands on the words that are drawn underneath.
   */
  renderPage: (options: {
    page: number;
    canvas: HTMLCanvasElement;
    textLayer: HTMLElement;
    /** CSS pixels the page should be drawn across; the scale follows from the page's own size. */
    width: number;
  }) => Promise<RenderedPdfPage>;
  /** Reads a page's text as one string, in the order the library reports its runs. */
  pageText: (page: number) => Promise<string>;
  destroy: () => Promise<void>;
}

export interface LoadPdfOptions {
  /** Where the bytes come from. Same-origin; the delivery token travels in the query. */
  url: string;
  signal?: AbortSignal;
}

/**
 * Parses a PDF. Rejects if the bytes cannot be fetched, are not a PDF, or if loading is aborted.
 *
 * The file is fetched **here, once, whole**, and the parser is handed the bytes rather than the
 * address. That is deliberate and it is the thing that makes reading in the browser safe to
 * offer at all: a delivery URL carries a short-lived capability token, and a parser left to
 * fetch its own byte ranges would keep asking for them for as long as somebody reads — under a
 * token that lapses part-way through, at which point the middle of a long document silently
 * stops arriving and there is no way to hand the parser a fresh address from outside it. One
 * fetch spends the token once, while it is certainly still valid.
 *
 * The cost is the whole file in memory, which is why the caller decides whether a document is
 * small enough to be read this way and falls back to pages drawn on the server when it is not.
 */
export async function loadPdf({ url, signal }: LoadPdfOptions): Promise<LoadedPdf> {
  const response = await fetch(url, { signal });
  if (!response.ok) {
    throw new Error(`the document could not be fetched (${response.status})`);
  }
  const data = await response.arrayBuffer();

  const task = pdfjs.getDocument({
    data,
    cMapUrl: PDFJS_CMAP_URL,
    cMapPacked: true,
    standardFontDataUrl: PDFJS_STANDARD_FONT_URL,
    wasmUrl: PDFJS_WASM_URL,
  });

  const abort = () => void task.destroy();
  signal?.addEventListener('abort', abort, { once: true });

  let doc: pdfjs.PDFDocumentProxy;
  try {
    doc = await task.promise;
  } finally {
    signal?.removeEventListener('abort', abort);
  }

  return {
    pageCount: doc.numPages,

    async renderPage({ page, canvas, textLayer, width }) {
      const pdfPage = await doc.getPage(page);
      const unscaled = pdfPage.getViewport({ scale: 1 });
      const scale = width / unscaled.width;
      const viewport = pdfPage.getViewport({ scale });

      // The canvas is drawn at the device's own resolution and shown at CSS size, so the page
      // is as sharp as the screen can be. The text layer is positioned in CSS pixels only —
      // it must line up with what is shown, not with what is drawn.
      const ratio = window.devicePixelRatio || 1;
      canvas.width = Math.floor(viewport.width * ratio);
      canvas.height = Math.floor(viewport.height * ratio);
      canvas.style.width = `${Math.floor(viewport.width)}px`;
      canvas.style.height = `${Math.floor(viewport.height)}px`;

      // The library owns the drawing context; the device-pixel factor is handed to it as a
      // transform rather than applied to a context of our own, because it will not accept both
      // a canvas and a context and the canvas is the supported half of that pair.
      const task = pdfPage.render({
        canvas,
        viewport,
        transform: ratio === 1 ? undefined : [ratio, 0, 0, ratio, 0, 0],
      });

      textLayer.replaceChildren();
      textLayer.style.width = `${Math.floor(viewport.width)}px`;
      textLayer.style.height = `${Math.floor(viewport.height)}px`;
      // The library's own stylesheet sizes each run against these, so they must be set on the
      // element it renders into rather than on anything wrapping it.
      textLayer.style.setProperty('--scale-factor', String(scale));
      textLayer.style.setProperty('--total-scale-factor', String(scale));

      const text = new pdfjs.TextLayer({
        textContentSource: pdfPage.streamTextContent(),
        container: textLayer,
        viewport,
      });

      await Promise.all([task.promise, text.render()]);

      return {
        width: viewport.width,
        height: viewport.height,
        dispose: () => {
          text.cancel();
          pdfPage.cleanup();
        },
      };
    },

    async pageText(page) {
      const pdfPage = await doc.getPage(page);
      const content = await pdfPage.getTextContent();
      return content.items
        .map((item) => ('str' in item ? item.str + (item.hasEOL ? '\n' : '') : ''))
        .join('');
    },

    // Closing the loading task is what releases the parser worker as well as the parsed
    // document; releasing the document alone would leave the worker running.
    destroy: () => task.destroy(),
  };
}
