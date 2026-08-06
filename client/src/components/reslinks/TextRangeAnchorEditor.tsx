// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useState } from 'react';
import { Alert, Flex, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useDocument, useFile, usePageText } from '../../api/hooks.ts';
import PdfView from '../../pdf/PdfView.tsx';
import { textRangeAnchorFor, type CapturedQuote } from '../../pdf/textSelection.ts';
import type { AnchorEditorProps } from './anchorTypes.ts';

/**
 * Anchoring a link to a passage: the reader opens the document, drags across the sentence,
 * and what is stored is the sentence plus where it sits in the text the server read.
 *
 * The two halves are deliberately different sources. The browser says *which words* — that is
 * all a selection can honestly say, because what it selected was laid out by a renderer, and
 * a renderer's own character positions do not survive the file being read again by anything
 * else. The offsets come from the server's extracted text, which is the stream every other
 * part of the system already resolves anchors against.
 *
 * When the words cannot be found in that stream the selection is refused, with the reason. It
 * is the case that matters most: it happens when the page has not been read yet, and when the
 * selected words were never text at all (a caption inside a scanned figure). An anchor stored
 * anyway would be a link pointing confidently at the wrong sentence, and a reader has no way
 * to tell that from a right one.
 */
export default function TextRangeAnchorEditor({ value, onChange, target }: AnchorEditorProps) {
  const { t } = useTranslation();
  const documentId = target?.targetType === 'document' ? target.targetId : undefined;
  const { data: document } = useDocument(documentId);
  const { data: file } = useFile(document?.currentFileId);

  const [captured, setCaptured] = useState<CapturedQuote | null>(null);
  // The words are looked up against the page they were taken from, which is not always the
  // page on screen by the time the answer comes back.
  const { data: pageText, isFetching, isError } = usePageText(file?.pagesUrl, captured?.page ?? null);

  // A cleared selection is not a retracted anchor. Reading on means clicking somewhere — into
  // the note field to say why these belong together, onto the page to turn it — and every one
  // of those collapses the browser's selection. Treating that as "never mind" would throw the
  // passage away at the exact moment the reader moved on to the next field, silently, leaving
  // a form that refuses to submit and does not say what changed. A passage is replaced by
  // selecting another one, and by nothing else.
  const onSelect = useCallback((selection: CapturedQuote | null) => {
    if (selection !== null) {
      setCaptured(selection);
    }
  }, []);

  useEffect(() => {
    if (captured === null || pageText === undefined) {
      return;
    }
    onChange(textRangeAnchorFor(captured, pageText.text));
    // onChange is the form's setter and is stable for the life of the dialog; adding it here
    // would re-run this on every keystroke elsewhere in the form.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [captured, pageText]);

  if (documentId === undefined) {
    return <Alert type="info" showIcon message={t('resLinks.anchorEditors.textNeedsDocument')} />;
  }

  const readable = file?.mimeType?.startsWith('application/pdf') === true && file.mayDownloadOriginal;
  if (file !== undefined && !readable) {
    return <Alert type="info" showIcon message={t('resLinks.anchorEditors.textNotSelectable')} />;
  }

  return (
    <Flex vertical gap={8} style={{ width: '100%', minWidth: 0 }}>
      <Typography.Text type="secondary">{t('resLinks.anchorEditors.textHint')}</Typography.Text>
      {file === undefined ? (
        <Flex justify="center" style={{ padding: 16 }}>
          <Spin />
        </Flex>
      ) : (
        <div style={{ maxHeight: 420, overflow: 'auto' }}>
          <PdfView url={file.contentUrl} onSelect={onSelect} />
        </div>
      )}

      {captured !== null && isFetching && <Spin size="small" />}
      {captured !== null && isError && (
        <Alert type="warning" showIcon message={t('resLinks.anchorEditors.textPageNotRead')} />
      )}
      {captured !== null && !isFetching && !isError && value === null && (
        <Alert type="warning" showIcon message={t('resLinks.anchorEditors.textNotFound')} />
      )}
      {isTextRange(value) && (
        <Alert
          type="success"
          showIcon
          message={t('resLinks.anchorEditors.textCaptured', {
            page: value.page,
            quote: truncate(value.quote),
          })}
        />
      )}
    </Flex>
  );
}

function isTextRange(value: unknown): value is { page: number; quote: string } {
  return (
    typeof value === 'object'
    && value !== null
    && typeof (value as { page?: unknown }).page === 'number'
    && typeof (value as { quote?: unknown }).quote === 'string'
  );
}

/** Enough of the quote to recognise it, on one line. */
function truncate(quote: string): string {
  const oneLine = quote.replace(/\s+/g, ' ').trim();
  return oneLine.length <= 60 ? oneLine : `${oneLine.slice(0, 59)}…`;
}
