// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { InboxOutlined, UploadOutlined } from '@ant-design/icons';
import { Alert, Button, Flex, Table, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useUnfiledDocuments, type CabinetInfo } from '../../api/hooks.ts';
import { formatSize } from '../attachments/fileFormat.ts';
import BulkFilingBar from './BulkFilingBar.tsx';
import UploadDrawer from './UploadDrawer.tsx';

export interface UnfiledDocumentsProps {
  cabinets: CabinetInfo[];
  canWrite: boolean;
}

/**
 * The inbox: everything the caller has added that is filed nowhere.
 *
 * <p>
 * This is the other half of "upload never blocks on deciding where something belongs". The
 * upload is one drop with no destination; this is where the deciding happens afterwards, in
 * bulk, when somebody has time.
 * </p>
 * <p>
 * It is not a shelf and there is deliberately no way to make it one. A document is unfiled
 * because no cabinet row names it, so filing removes it from here by itself and nothing has to
 * be kept in step.
 * </p>
 */
export default function UnfiledDocuments({ cabinets, canWrite }: UnfiledDocumentsProps) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [selected, setSelected] = useState<string[]>([]);
  const [uploading, setUploading] = useState(false);

  const { data, isFetching } = useUnfiledDocuments({ page, pageSize });

  return (
    <>
      <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
        <Typography.Title level={4} style={{ margin: 0 }}>
          <InboxOutlined /> {t('cabinets.unfiled')}
        </Typography.Title>
        {canWrite && (
          <Button icon={<UploadOutlined />} onClick={() => setUploading(true)}>
            {t('uploads.upload')}
          </Button>
        )}
      </Flex>

      <Alert
        type="info"
        showIcon
        style={{ marginBottom: 12 }}
        title={t('cabinets.unfiledHint')}
      />

      {canWrite && selected.length > 0 && (
        <BulkFilingBar
          documentIds={selected}
          cabinets={cabinets}
          onDone={() => setSelected([])}
        />
      )}

      <Table
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !data}
        dataSource={data?.items}
        locale={{ emptyText: t('cabinets.unfiledEmpty') }}
        rowSelection={
          canWrite
            ? {
                selectedRowKeys: selected,
                onChange: (keys) => setSelected(keys.map(String)),
              }
            : undefined
        }
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 20);
        }}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('documents.title'),
            dataIndex: 'title',
            ellipsis: true,
            render: (value: string, row: { id: string }) => (
              <Link to={`/documents/${row.id}`}>{value}</Link>
            ),
          },
          {
            title: t('documents.visibility'),
            dataIndex: 'visibility',
            width: 160,
            render: (value: string) => <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>,
          },
          {
            title: t('cabinets.size'),
            dataIndex: 'sizeBytes',
            width: 110,
            align: 'right',
            render: (value: number | null) => (value === null ? '' : formatSize(value)),
          },
        ]}
      />

      {/* Mounted on demand, so a closed drawer does not ask the server about limits. */}
      {uploading && <UploadDrawer open onClose={() => setUploading(false)} />}
    </>
  );
}
