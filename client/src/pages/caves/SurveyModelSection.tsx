// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EyeOutlined, UploadOutlined } from '@ant-design/icons';
import { App, Button, Card, Flex, Modal, Popconfirm, Table, Tag, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useDeleteSurveyModel,
  useSurveyModels,
  useUploadSurveyModel,
  type SurveyModelInfo,
} from '../../api/hooks.ts';
import CaveViewPanel from '../../components/caveview/CaveViewPanel.tsx';

/** File name handed to the 3D viewer — its extension selects the survey parser. */
function viewerFileName(model: SurveyModelInfo): string {
  return `${model.name}.${model.format === 'lox' ? 'lox' : '3d'}`;
}

/**
 * 3D survey models of a cave (.lox/.3d): list, upload and the embedded viewer. The
 * server already withholds models of location-protected caves from callers without the
 * exact-location permission, so an empty list here needs no special casing.
 */
export default function SurveyModelSection({ caveId, canEdit }: { caveId: string; canEdit: boolean }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: models } = useSurveyModels(caveId);
  const upload = useUploadSurveyModel();
  const remove = useDeleteSurveyModel();
  const [viewing, setViewing] = useState<SurveyModelInfo | null>(null);

  const onUpload = async (file: File) => {
    try {
      await upload.mutateAsync({ caveId, file });
      message.success(t('surveyModels.uploaded'));
    } catch {
      message.error(t('surveyModels.uploadFailed'));
    }
  };

  return (
    <Card
      title={t('surveyModels.title')}
      style={{ marginTop: 16 }}
      extra={
        canEdit && (
          <Upload
            accept=".lox,.3d"
            showUploadList={false}
            beforeUpload={(file) => {
              void onUpload(file);
              return false;
            }}
          >
            <Button size="small" icon={<UploadOutlined />} loading={upload.isPending}>
              {t('surveyModels.upload')}
            </Button>
          </Upload>
        )
      }
    >
      <Table<SurveyModelInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        dataSource={models}
        pagination={false}
        locale={{ emptyText: t('surveyModels.empty') }}
        columns={[
          { title: t('caves.name'), dataIndex: 'name' },
          {
            title: t('surveyModels.format'),
            dataIndex: 'format',
            width: 110,
            render: (format: string) => <Tag>{format === 'lox' ? 'Therion .lox' : 'Survex .3d'}</Tag>,
          },
          { title: t('surveyModels.surveyedAt'), dataIndex: 'surveyedAt', width: 120 },
          {
            key: 'actions',
            width: 140,
            render: (_, model) => (
              <Flex gap={4}>
                <Button size="small" icon={<EyeOutlined />} onClick={() => setViewing(model)}>
                  {t('surveyModels.view')}
                </Button>
                {canEdit && (
                  <Popconfirm
                    title={t('surveyModels.deleteConfirm')}
                    onConfirm={() => void remove.mutateAsync({ id: model.id, caveId })}
                  >
                    <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                )}
              </Flex>
            ),
          },
        ]}
      />

      <Modal
        title={viewing?.name}
        open={viewing !== null}
        onCancel={() => setViewing(null)}
        footer={null}
        width="min(1200px, 95vw)"
        destroyOnHidden
      >
        {viewing && (
          <CaveViewPanel fileUrl={viewing.modelUrl} fileName={viewerFileName(viewing)} height="70vh" />
        )}
      </Modal>
    </Card>
  );
}
