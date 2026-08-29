// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EyeOutlined, UploadOutlined } from '@ant-design/icons';
import { App, Button, Card, Flex, Modal, Popconfirm, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  surveyModelReadableByViewer,
  surveyModelUnsettled,
  useDeleteSurveyModel,
  useSurveyModels,
  type SurveyModelInfo,
} from '../../api/hooks.ts';
import CaveViewPanel from '../../components/caveview/CaveViewPanel.tsx';
import SurveyModelUploadModal from './SurveyModelUploadModal.tsx';

/** File name handed to the survey viewer — its extension selects the parser. */
function viewerFileName(model: SurveyModelInfo): string {
  return `${model.name}.${model.format === 'lox' ? 'lox' : '3d'}`;
}

/**
 * What each format is called in the list. The two line-plot names are product names and read the
 * same in every language; what a wall mesh is called is an ordinary word and is translated, so a
 * Romanian reader is not shown one English noun in a column beside three translated ones.
 */
const FORMAT_LABEL_KEYS: Record<SurveyModelInfo['format'], string> = {
  lox: 'surveyModels.formats.lox',
  survex3d: 'surveyModels.formats.survex3d',
  stl: 'surveyModels.formats.stl',
};

/**
 * 3D survey models of a cave: list, upload and the embedded viewer. The server already withholds
 * models of location-protected caves from callers without the exact-location permission, so an
 * empty list here needs no special casing.
 *
 * A wall mesh is not drawable when it lands — a conversion runs after the upload returns and can
 * fail — so this list says where each one has got to, and keeps asking until nothing is left to
 * wait for.
 */
export default function SurveyModelSection({ caveId, canEdit }: { caveId: string; canEdit: boolean }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: models } = useSurveyModels(caveId);
  const remove = useDeleteSurveyModel();
  const [viewing, setViewing] = useState<SurveyModelInfo | null>(null);
  const [uploading, setUploading] = useState(false);

  const stateCell = (model: SurveyModelInfo) => {
    if (model.status === 'failed') {
      return (
        <Flex vertical gap={2}>
          <Tag color="error">{t('surveyModels.statusValues.failed')}</Tag>
          {/* The server's own words: it knows why this file could not be read, and no phrase
              written here in advance could say it as precisely. */}
          <Typography.Text type="danger" style={{ fontSize: 12 }}>
            {model.processingError ?? t('surveyModels.conversionFailed')}
          </Typography.Text>
        </Flex>
      );
    }
    if (surveyModelUnsettled(model.status)) {
      return (
        <Flex vertical gap={2}>
          <Tag color="processing">{t(`surveyModels.statusValues.${model.status}`)}</Tag>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t('surveyModels.converting')}
          </Typography.Text>
        </Flex>
      );
    }
    return (
      <Flex vertical gap={2}>
        <Tag color="success">{t('surveyModels.statusValues.ready')}</Tag>
        {model.triangleCount !== null && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t('surveyModels.triangles', { count: model.triangleCount })}
          </Typography.Text>
        )}
        {model.sourcePrecisionLost && (
          <Tooltip title={t('surveyModels.precisionLostHint')}>
            <Typography.Text type="warning" style={{ fontSize: 12 }}>
              {t('surveyModels.precisionLost')}
            </Typography.Text>
          </Tooltip>
        )}
      </Flex>
    );
  };

  return (
    <Card
      title={t('surveyModels.title')}
      style={{ marginTop: 16 }}
      extra={
        canEdit && (
          <Button size="small" icon={<UploadOutlined />} onClick={() => setUploading(true)}>
            {t('surveyModels.upload')}
          </Button>
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
            render: (format: SurveyModelInfo['format']) => <Tag>{t(FORMAT_LABEL_KEYS[format])}</Tag>,
          },
          { title: t('surveyModels.surveyedAt'), dataIndex: 'surveyedAt', width: 120 },
          {
            title: t('surveyModels.status'),
            key: 'status',
            width: 220,
            render: (_, model) => stateCell(model),
          },
          {
            key: 'actions',
            width: 140,
            render: (_, model) => (
              <Flex gap={4}>
                {surveyModelReadableByViewer(model) && (
                  <Button size="small" icon={<EyeOutlined />} onClick={() => setViewing(model)}>
                    {t('surveyModels.view')}
                  </Button>
                )}
                {canEdit && (
                  <Popconfirm
                    title={t('surveyModels.deleteConfirm')}
                    onConfirm={async () => {
                      try {
                        await remove.mutateAsync({ id: model.id, caveId });
                      } catch {
                        message.error(t('common.saveFailed'));
                      }
                    }}
                  >
                    <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                )}
              </Flex>
            ),
          },
        ]}
      />

      {canEdit && (
        <SurveyModelUploadModal
          caveId={caveId}
          open={uploading}
          onClose={() => setUploading(false)}
        />
      )}

      <Modal
        title={viewing?.name}
        open={viewing !== null}
        onCancel={() => setViewing(null)}
        footer={null}
        width="min(1200px, 95vw)"
        destroyOnHidden
      >
        {viewing && (
          <CaveViewPanel
            fileUrl={viewing.modelUrl}
            fileName={viewerFileName(viewing)}
            height="70vh"
            surveyModelId={viewing.id}
          />
        )}
      </Modal>
    </Card>
  );
}
