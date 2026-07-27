// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined, UploadOutlined } from '@ant-design/icons';
import { App, Button, Card, Popconfirm, Table, Tag, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCenterlines,
  useDeleteCenterline,
  useUploadCenterline,
  type CenterlineInfo,
} from '../../api/hooks.ts';

/**
 * Cave centerlines: uploaded GeoJSON/GPX line work shown as a map overlay. The server
 * withholds centerlines of location-protected caves from callers without the
 * exact-location permission, so an empty list here needs no special casing.
 */
export default function CenterlineSection({ caveId, canEdit }: { caveId: string; canEdit: boolean }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: centerlines } = useCenterlines(caveId);
  const upload = useUploadCenterline();
  const remove = useDeleteCenterline();

  const onUpload = async (file: File) => {
    try {
      await upload.mutateAsync({ caveId, file });
      message.success(t('centerlines.uploaded'));
    } catch {
      message.error(t('centerlines.uploadFailed'));
    }
  };

  return (
    <Card
      title={t('centerlines.title')}
      style={{ marginTop: 16 }}
      extra={
        canEdit && (
          <Upload
            accept=".gpx,.geojson,.json"
            showUploadList={false}
            beforeUpload={(file) => {
              void onUpload(file);
              return false;
            }}
          >
            <Button size="small" icon={<UploadOutlined />} loading={upload.isPending}>
              {t('centerlines.upload')}
            </Button>
          </Upload>
        )
      }
    >
      <Table<CenterlineInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        dataSource={centerlines}
        pagination={false}
        locale={{ emptyText: t('centerlines.empty') }}
        columns={[
          { title: t('caves.name'), dataIndex: 'name' },
          {
            title: t('centerlines.length'),
            dataIndex: 'lengthM',
            width: 130,
            align: 'right',
            render: (lengthM: number | null) =>
              lengthM == null ? '' : `${Number(lengthM).toLocaleString()} m`,
          },
          {
            title: t('centerlines.source'),
            dataIndex: 'source',
            width: 120,
            render: (source: string) => <Tag>{t(`centerlines.sourceValues.${source}`)}</Tag>,
          },
          ...(canEdit
            ? [
                {
                  key: 'actions',
                  width: 60,
                  render: (_: unknown, centerline: CenterlineInfo) => (
                    <Popconfirm
                      title={t('centerlines.deleteConfirm')}
                      onConfirm={() => void remove.mutateAsync({ id: centerline.id, caveId })}
                    >
                      <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                    </Popconfirm>
                  ),
                },
              ]
            : []),
        ]}
      />
    </Card>
  );
}
