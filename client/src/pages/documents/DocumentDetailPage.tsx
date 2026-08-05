// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { Alert, Breadcrumb, Card, Descriptions, Flex, Spin, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  useCabinets,
  useCan,
  useDocument,
  useDocumentTypes,
  useFile,
} from '../../api/hooks.ts';
import DocumentMetadata from '../../components/attachments/DocumentMetadata.tsx';
import FileVersions from '../../components/attachments/FileVersions.tsx';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import DocumentContent from '../../components/documents/DocumentContent.tsx';
import DownloadDocument from '../../components/documents/DownloadDocument.tsx';
import TextState from '../../components/documents/TextState.tsx';
import LinksSection from '../../components/reslinks/LinksSection.tsx';
import { useIsMobile } from '../../hooks/useIsMobile.ts';

/**
 * A document's own page: what it is, which of its versions is current, where it is filed,
 * what came of reading its text, and the document itself.
 *
 * It exists because every route into a document used to end in a download. A content search
 * can find the one paragraph that matters inside a two-hundred-page report, and the only
 * thing the interface could do with that was hand over a file — which is not reading it, and
 * loses the passage that was found. Downloading is still offered; it is no longer the only
 * thing on offer.
 *
 * Rights here are the caller's domain-level ones, which is coarser than the server's answer:
 * a document is not addressable by the per-object access endpoint, so the page hides controls
 * the server would certainly refuse and lets it refuse the rest. Nothing is decided here.
 */
export default function DocumentDetailPage() {
  const { t, i18n } = useTranslation();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const isMobile = useIsMobile();
  // A content search knows which page matched; arriving here without one is simply arriving
  // at the beginning, so anything that is not a page number is read as none.
  const [searchParams] = useSearchParams();
  const requestedPage = Number.parseInt(searchParams.get('page') ?? '', 10);

  const { data: document, isPending, isError } = useDocument(id);
  // The document says what it is; the file says how to fetch it and how far this caller may
  // reach for the bytes. Two requests rather than one because the second mints a token.
  const { data: file } = useFile(document?.currentFileId);
  const { data: types } = useDocumentTypes();

  const mayWrite = useCan('documents', 'write');
  const mayReadDocuments = useCan('documents', 'read');
  // The tree is small enough to fetch whole, and it is the only thing that can turn a
  // filing into a name. A caller who may not list it simply sees no filing section.
  const { data: cabinets } = useCabinets(mayReadDocuments);

  const filedIn = useMemo(() => {
    if (!document || !cabinets) {
      return [];
    }
    const names = new Map(cabinets.map((cabinet) => [cabinet.id, cabinet.name]));
    return cabinets
      .filter((cabinet) => document.cabinetIds.includes(cabinet.id))
      .map((cabinet) => ({
        id: cabinet.id,
        // Names are unique only among siblings — "1987" sits under many archives — so the
        // whole path is the only unambiguous way to say where something is filed.
        path: cabinet.ancestorIds.map((ancestorId) => names.get(ancestorId) ?? '…').join(' / '),
      }));
  }, [document, cabinets]);

  if (isError) {
    return (
      <div style={{ padding: 24 }}>
        <Alert type="error" showIcon message={t('common.loadFailed')} />
      </div>
    );
  }

  if (isPending || !document) {
    return (
      <Flex align="center" justify="center" style={{ height: '100%' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const type = types?.find((candidate) => candidate.id === document.documentTypeId);

  return (
    // `minWidth: 0` so a wide child — a page picture, a delimited table read as text — scrolls
    // or shrinks inside its own panel instead of widening the page under it. On a phone the
    // margins give back the width they cost.
    <div style={{ padding: isMobile ? 12 : 24, maxWidth: 1100, minWidth: 0 }}>
      <Breadcrumb
        items={[
          {
            title: (
              <Typography.Link onClick={() => navigate('/cabinets')}>
                {t('cabinets.title')}
              </Typography.Link>
            ),
          },
          { title: document.title },
        ]}
      />

      <Flex justify="space-between" align="center" gap={12} wrap style={{ marginTop: 8 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {document.title}
        </Typography.Title>
        {/*
          On a narrow screen the title takes the whole row, so the controls line up under it
          from the same edge rather than stranded against the right one.
        */}
        <Flex gap={8} wrap justify={isMobile ? 'start' : 'end'} align="center">
          {mayWrite && <DocumentMetadata documentId={document.id} />}
          <FileVersions
            fileId={document.currentFileId}
            versionNumber={document.currentVersionNumber}
            canEdit={mayWrite}
          />
          {file && <DownloadDocument file={file} />}
        </Flex>
      </Flex>

      <Card size="small" style={{ marginTop: 16 }}>
        <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small">
          <Descriptions.Item label={t('documents.type')}>
            {type?.name ?? t('documents.noType')}
          </Descriptions.Item>
          <Descriptions.Item label={t('documents.visibility')}>
            <Tag>{t(`caves.visibilityValues.${document.visibility}`)}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label={t('documents.format')}>{document.mimeType}</Descriptions.Item>
          <Descriptions.Item label={t('documents.size')}>
            {formatSize(document.sizeBytes)}
          </Descriptions.Item>
          <Descriptions.Item label={t('documents.version')}>
            {document.currentVersionNumber}
          </Descriptions.Item>
          <Descriptions.Item label={t('documents.language')}>
            {document.language === null
              ? t('documents.languageUnknown')
              : (i18n.exists(`documents.languages.${document.language}`)
                  ? t(`documents.languages.${document.language}`)
                  : document.language)}
          </Descriptions.Item>
          {document.author !== null && (
            <Descriptions.Item label={t('documents.author')}>{document.author}</Descriptions.Item>
          )}
          <Descriptions.Item label={t('documents.updated')}>
            {new Date(document.updatedAt).toLocaleString(i18n.resolvedLanguage)}
          </Descriptions.Item>
          <Descriptions.Item label={t('documents.text')} span={{ xs: 1, sm: 2, md: 3 }}>
            <TextState state={document.textExtraction} fontSize={14} />
          </Descriptions.Item>
          {mayReadDocuments && (
            <Descriptions.Item label={t('documents.cabinets')} span={{ xs: 1, sm: 2, md: 3 }}>
              {filedIn.length === 0 ? (
                <Typography.Text type="secondary">{t('documents.notFiled')}</Typography.Text>
              ) : (
                <Flex gap={6} wrap>
                  {filedIn.map((cabinet) => (
                    <Tag key={cabinet.id}>{cabinet.path}</Tag>
                  ))}
                </Flex>
              )}
            </Descriptions.Item>
          )}
        </Descriptions>
      </Card>

      {/*
        The content itself. A search hit arrives with the matched page in the query string
        where the format numbers pages at all; the renderer opens there.
      */}
      <Card size="small" title={t('documents.viewer.title')} style={{ marginTop: 16 }}>
        {file ? (
          <DocumentContent
            file={file}
            pageCount={document.pageCount}
            initialPage={Number.isFinite(requestedPage) && requestedPage > 0 ? requestedPage : 1}
          />
        ) : (
          <Spin />
        )}
      </Card>

      {/*
        What this document takes part in, as a section of its own rather than tucked into the
        editing panel above: the links are as much a fact about the document as its format is,
        and a reader who may not edit anything here still has every reason to see them. It sits
        under the document because the document is why the page exists.
      */}
      <LinksSection entityType="document" entityId={document.id} canAdd entityTitle={document.title} />
    </div>
  );
}
