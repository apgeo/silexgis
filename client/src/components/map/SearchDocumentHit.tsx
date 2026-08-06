// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { SearchDocumentItem } from '../../api/hooks.ts';
import { splitSnippet } from './searchSnippet.ts';

const { Text } = Typography;

/**
 * One content hit: the document that matched, the stretch of it that matched, and — only
 * where something actually numbers a division — which division that stretch came from.
 *
 * The division is not decoration. It describes the artifact whose pages this installation
 * draws, because that is the artifact the words were read out of: a PDF has pages, and so
 * does the portable copy a converter makes of a word-processing document, while a workbook
 * read as it stands has sheets and a deck has slides. Where nothing numbers anything the
 * whole text arrives as one row however long it is, and printing "page 1" against a
 * forty-page report would be this interface inventing a fact the file never stated.
 */
export default function SearchDocumentHit({ hit }: { hit: SearchDocumentItem }) {
  const { t } = useTranslation();
  const parts = splitSnippet(hit.snippet);

  const where =
    hit.division === 'whole' ? null : t(`search.divisions.${hit.division}`, { number: hit.pageNumber });

  return (
    <div style={{ paddingBlock: 2 }}>
      <div>
        <Text strong>{hit.title}</Text>
        {where ? (
          <Text type="secondary" style={{ marginInlineStart: 8, fontSize: 12 }}>
            {where}
          </Text>
        ) : null}
        {!hit.isCurrentVersion ? (
          <Text type="secondary" style={{ marginInlineStart: 8, fontSize: 12 }}>
            {t('search.supersededVersion', { number: hit.versionNumber })}
          </Text>
        ) : null}
      </div>
      {parts.length > 0 ? (
        <Text type="secondary" style={{ fontSize: 12, whiteSpace: 'normal' }}>
          {parts.map((part, index) =>
            part.matched ? <mark key={index}>{part.text}</mark> : <span key={index}>{part.text}</span>,
          )}
        </Text>
      ) : null}
    </div>
  );
}
