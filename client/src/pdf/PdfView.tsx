// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { LeftOutlined, RightOutlined, ZoomInOutlined, ZoomOutOutlined } from '@ant-design/icons';
import { Alert, Button, Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { capturedQuoteFrom, type CapturedQuote } from './textSelection.ts';
import type { LoadedPdf } from './pdfEngine.ts';

/**
 * A PDF read in the browser, one page at a time, with its words where the words are.
 *
 * It exists beside the server-drawn page pictures rather than instead of them, and the two
 * answer different questions. A picture of a page is what someone who may not have the file
 * is shown — it discloses what the page shows and nothing else, which is what makes reading a
 * document safe to offer to a caller the bytes are withheld from. This draws the file itself,
 * so it is only ever reached for by a caller who may already have it, and what it adds is the
 * thing a picture structurally cannot have: text that can be selected, searched by the browser
 * and read aloud by a screen reader.
 *
 * The library is loaded on first use and never before, so a session that opens no PDF pays
 * nothing for this. That is also why the engine sits behind its own module: nothing here, and
 * nothing above here, names the library.
 *
 * Selection is offered but never assumed. `onSelect` is called with the words and their
 * surroundings on every settled selection, and with null when the selection is cleared; a
 * caller that passes none gets an ordinary read-only viewer, with the text layer still present
 * because that is what makes the text selectable, copyable and findable at all.
 */
export default function PdfView({
  url,
  initialPage = 1,
  onSelect,
  onPageChange,
}: {
  /** Where the bytes are. Same-origin, carrying whatever delivery token the caller was given. */
  url: string;
  initialPage?: number;
  onSelect?: (selection: CapturedQuote | null) => void;
  onPageChange?: (page: number) => void;
}) {
  const { t } = useTranslation();
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const textLayerRef = useRef<HTMLDivElement>(null);
  const frameRef = useRef<HTMLDivElement>(null);

  const [pdf, setPdf] = useState<LoadedPdf | null>(null);
  const [page, setPage] = useState(Math.max(1, initialPage));
  const [zoom, setZoom] = useState(1);
  const [failed, setFailed] = useState(false);
  const [drawing, setDrawing] = useState(true);
  // Measured from the frame rather than assumed, so the page fits a phone and a wide desktop
  // pane without either being told about the other.
  const [frameWidth, setFrameWidth] = useState(0);

  // ---- load ---------------------------------------------------------------
  useEffect(() => {
    const controller = new AbortController();
    let loaded: LoadedPdf | null = null;
    setPdf(null);
    setFailed(false);
    setDrawing(true);

    void (async () => {
      try {
        const { loadPdf } = await import('./pdfEngine.ts');
        loaded = await loadPdf({ url, signal: controller.signal });
        if (!controller.signal.aborted) {
          setPdf(loaded);
        }
      } catch {
        if (!controller.signal.aborted) {
          setFailed(true);
          setDrawing(false);
        }
      }
    })();

    return () => {
      controller.abort();
      void loaded?.destroy().catch(() => {});
    };
  }, [url]);

  // ---- measure ------------------------------------------------------------
  useLayoutEffect(() => {
    const frame = frameRef.current;
    if (frame === null) {
      return;
    }
    const observer = new ResizeObserver(([entry]) => setFrameWidth(entry.contentRect.width));
    observer.observe(frame);
    setFrameWidth(frame.clientWidth);
    return () => observer.disconnect();
  }, []);

  // A search hit names the page it matched; following a second hit in the same document has to
  // move the reader rather than leave them where the first one landed.
  useEffect(() => setPage(Math.max(1, initialPage)), [initialPage]);

  // ---- draw ---------------------------------------------------------------
  useEffect(() => {
    const canvas = canvasRef.current;
    const textLayer = textLayerRef.current;
    if (pdf === null || canvas === null || textLayer === null || frameWidth === 0) {
      return;
    }

    let stale = false;
    let rendered: { dispose: () => void } | null = null;
    setDrawing(true);

    void (async () => {
      try {
        const result = await pdf.renderPage({
          page: Math.min(page, pdf.pageCount),
          canvas,
          textLayer,
          width: Math.max(200, frameWidth * zoom),
        });
        if (stale) {
          result.dispose();
          return;
        }
        rendered = result;
        setDrawing(false);
      } catch {
        if (!stale) {
          setFailed(true);
          setDrawing(false);
        }
      }
    })();

    return () => {
      stale = true;
      rendered?.dispose();
    };
  }, [pdf, page, zoom, frameWidth]);

  useEffect(() => onPageChange?.(page), [page, onPageChange]);

  // ---- selection ----------------------------------------------------------
  const publishSelection = useCallback(() => {
    const layer = textLayerRef.current;
    if (onSelect === undefined || layer === null) {
      return;
    }
    onSelect(capturedQuoteFrom(document.getSelection(), layer, page));
  }, [onSelect, page]);

  useEffect(() => {
    if (onSelect === undefined) {
      return;
    }
    // Read on settle rather than on every change: a drag fires selectionchange per character,
    // and a caller that has to look words up would then look up every prefix of the phrase.
    document.addEventListener('mouseup', publishSelection);
    document.addEventListener('touchend', publishSelection);
    document.addEventListener('keyup', publishSelection);
    return () => {
      document.removeEventListener('mouseup', publishSelection);
      document.removeEventListener('touchend', publishSelection);
      document.removeEventListener('keyup', publishSelection);
    };
  }, [onSelect, publishSelection]);

  if (failed) {
    return <Alert type="warning" showIcon title={t('documents.viewer.pdfFailed')} />;
  }

  const pageCount = pdf?.pageCount ?? null;

  return (
    <Flex vertical gap={12} align="center" style={{ width: '100%', minWidth: 0 }}>
      <div ref={frameRef} style={{ position: 'relative', width: '100%', minWidth: 0 }}>
        {(pdf === null || drawing) && (
          <Flex justify="center" style={{ padding: 24 }}>
            <Spin />
          </Flex>
        )}
        {/* The canvas and the text runs are one stack: the runs are transparent and sit exactly
            over the glyphs they describe, which is what makes a drag across the page select the
            words that are drawn there. */}
        <div style={{ position: 'relative', display: pdf === null ? 'none' : 'inline-block' }}>
          <canvas ref={canvasRef} style={{ display: 'block', maxWidth: '100%' }} />
          <div
            ref={textLayerRef}
            className="textLayer"
            style={{ position: 'absolute', inset: 0 }}
            data-testid="pdf-text-layer"
          />
        </div>
      </div>

      <Flex gap={8} align="center" wrap justify="center">
        <Button
          icon={<LeftOutlined />}
          disabled={page <= 1}
          onClick={() => setPage((current) => Math.max(1, current - 1))}
          aria-label={t('documents.viewer.previousPage')}
        />
        <Typography.Text>
          {pageCount === null
            ? t('documents.viewer.pageAlt', { number: page })
            : t('documents.viewer.pageOf', { number: page, count: pageCount })}
        </Typography.Text>
        <Button
          icon={<RightOutlined />}
          disabled={pageCount !== null && page >= pageCount}
          onClick={() => setPage((current) => (pageCount === null ? current : Math.min(pageCount, current + 1)))}
          aria-label={t('documents.viewer.nextPage')}
        />
        <Button
          icon={<ZoomOutOutlined />}
          disabled={zoom <= 0.5}
          onClick={() => setZoom((z) => Math.max(0.5, Math.round((z - 0.25) * 100) / 100))}
          aria-label={t('documents.viewer.zoomOut')}
        />
        <Button
          icon={<ZoomInOutlined />}
          disabled={zoom >= 4}
          onClick={() => setZoom((z) => Math.min(4, Math.round((z + 0.25) * 100) / 100))}
          aria-label={t('documents.viewer.zoomIn')}
        />
      </Flex>
    </Flex>
  );
}
