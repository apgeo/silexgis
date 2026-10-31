// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { ArrowLeftOutlined, UndoOutlined } from '@ant-design/icons';
import { App, Empty, Flex, Popconfirm, Table, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';
import { ApiError } from '../../api/client.ts';
import {
  useDeletedDocuments,
  useDocumentConfig,
  useRestoreDocument,
  type DeletedDocument,
} from '../../api/hooks.ts';
import { daysLeft, formatDays } from '../../components/trips/tripRestoreWindow.ts';

/**
 * The deleted documents this reader may put back, most recently deleted first.
 *
 * Which documents those are is the server's decision, made row by row: the right that edits a
 * document is the right that deletes and restores it, asked of the document as it stands. So
 * nothing here filters, and an account that may edit nothing sees an empty list rather than a
 * refusal.
 *
 * The one act the page offers is restoring. A deleted document answers as not found everywhere
 * else, so there is nothing to open and no row is a link; once a document is back the page opens
 * it, because seeing it whole is the quickest proof that it came back whole.
 */
export default function DeletedDocumentsPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const { data, isFetching, isError } = useDeletedDocuments(page);
  const { data: config } = useDocumentConfig();
  const restore = useRestoreDocument();
  // Read once: the days left are whole days, and a figure that moved under the reader while the
  // page sat open would be a figure about the render rather than about the document.
  const [now] = useState(() => new Date());
  const language = i18n.resolvedLanguage;

  const onRestore = async (document: DeletedDocument) => {
    try {
      await restore.mutateAsync(document.id);
      message.success(t('documents.deletion.restored'));
      navigate(`/documents/${document.id}`);
    } catch (e) {
      message.error(t(restoreProblemKey(e)));
    }
  };

  const removedText = (document: DeletedDocument): string => {
    const left = daysLeft(document.restorableUntil, now);
    if (left === null) {
      return t('documents.deletion.kept');
    }
    return left === 0
      ? t('documents.deletion.removedToday')
      : t('documents.deletion.removedIn', { period: formatDays(left, language) });
  };

  // Nothing is said while the answer is still on its way: a sentence about a window nobody has
  // read yet would be a guess, and this is the page where the number matters.
  const windowText =
    config === undefined
      ? null
      : config.deletedRetentionDays === null
        ? t('documents.deletion.introKept')
        : t('documents.deletion.introWindow', {
            period: formatDays(config.deletedRetentionDays, language),
          });

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 8 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('documents.deletion.title')}
        </Typography.Title>
        <Link to="/cabinets">
          <Button icon={<ArrowLeftOutlined />}>{t('cabinets.title')}</Button>
        </Link>
      </Flex>
      <Typography.Paragraph type="secondary" style={{ maxWidth: 760 }}>
        {t('documents.deletion.intro')}
        {windowText && <> {windowText}</>}
      </Typography.Paragraph>
      <Table<DeletedDocument>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        data-testid="deleted-documents"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={(pagination: TablePaginationConfig) => setPage(pagination.current ?? 1)}
        locale={{
          emptyText: (
            <Empty
              data-testid="deleted-documents-empty"
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={isError ? t('documents.deletion.loadFailed') : t('documents.deletion.empty')}
            />
          ),
        }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: false,
          hideOnSinglePage: true,
        }}
        columns={[
          { title: t('documents.deletion.columns.title'), dataIndex: 'title', ellipsis: true },
          {
            title: t('documents.deletion.columns.deletedAt'),
            dataIndex: 'deletedAt',
            width: 190,
            render: (value: string) => new Date(value).toLocaleString(language),
          },
          {
            title: t('documents.deletion.columns.deletedBy'),
            dataIndex: 'deletedByName',
            width: 180,
          },
          {
            title: t('documents.deletion.columns.removed'),
            key: 'removed',
            width: 170,
            render: (_, document) => removedText(document),
          },
          {
            title: '',
            key: 'actions',
            width: 130,
            render: (_, document) => (
              <Popconfirm
                title={t('documents.deletion.restoreConfirm')}
                description={t('documents.deletion.restoreConfirmDetail')}
                okText={t('documents.deletion.restore')}
                onConfirm={() => void onRestore(document)}
              >
                <Button
                  size="small"
                  icon={<UndoOutlined />}
                  loading={restore.isPending && restore.variables === document.id}
                  data-testid="deleted-document-restore"
                >
                  {t('documents.deletion.restore')}
                </Button>
              </Popconfirm>
            ),
          },
        ]}
      />
    </div>
  );
}

/**
 * The sentence for a restore that was refused. Each refusal the route names means something a
 * reader can act on — the window ran out, somebody else already restored it, it is not theirs —
 * and anything else is the ordinary failed save.
 */
function restoreProblemKey(error: unknown): string {
  if (!(error instanceof ApiError)) {
    return 'common.saveFailed';
  }
  if (error.code === 'document.restore_window_passed') {
    return 'documents.deletion.windowPassed';
  }
  if (error.code === 'document.not_deleted' || error.status === 404) {
    return 'documents.deletion.gone';
  }
  return error.status === 403 ? 'documents.deletion.restoreForbidden' : 'common.saveFailed';
}
