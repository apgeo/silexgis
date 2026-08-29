// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined, DownloadOutlined, UploadOutlined } from '@ant-design/icons';
import { App, Button, Card, Popconfirm, Table, Tag, Tooltip, Typography, Upload } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useDeleteSurveySource,
  useSurveySources,
  useUploadSurveySource,
  type SurveySourceInfo,
} from '../../api/hooks.ts';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import { surveySourceProblemMessage } from './surveySourceProblems.ts';

/**
 * What the archive accepts, kept in step with the server's own list. The server is the authority
 * and refuses anything else with a code this page has wording for; this only spares somebody the
 * round trip, and — because it is a hint to the file picker rather than a gate — a file chosen
 * past it is still refused where refusals belong.
 */
export const ACCEPTED_SOURCE_EXTENSIONS = '.th,.th2,.thconfig,.svx,.log,.zip';

/** The cap the server refuses past. */
const MAX_SOURCE_BYTES = 100 * 1024 * 1024;

/**
 * What each archived kind is called in the list. The tool names are product names and read the
 * same in every language; the nouns around them are ordinary words and are translated.
 */
const KIND_LABEL_KEYS: Record<SurveySourceInfo['kind'], string> = {
  therionSource: 'surveySources.kinds.therionSource',
  therionConfig: 'surveySources.kinds.therionConfig',
  therionLog: 'surveySources.kinds.therionLog',
  survexSource: 'surveySources.kinds.survexSource',
  topoDroidArchive: 'surveySources.kinds.topoDroidArchive',
};

/**
 * The raw survey material a cave's compiled models were made from: the survey languages, a project
 * configuration, a survey app's export bundle, and the log a compilation wrote.
 *
 * Separate from the models list on purpose. A model is drawn and read; these are neither — they are
 * kept so the survey can be compiled again when the toolchain that produced the export has moved
 * on, and nothing here opens one. The server withholds the whole list for a location-protected cave
 * from callers without the exact-location permission, so an empty list needs no special casing.
 */
export default function SurveySourceSection({ caveId, canEdit }: { caveId: string; canEdit: boolean }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: sources } = useSurveySources(caveId);
  const upload = useUploadSurveySource();
  const remove = useDeleteSurveySource();

  const onUpload = async (file: File) => {
    if (file.size > MAX_SOURCE_BYTES) {
      message.error(t('surveySources.problems.sizeInvalid'));
      return;
    }
    try {
      await upload.mutateAsync({ caveId, file });
      message.success(t('surveySources.uploaded'));
    } catch (error) {
      // The server's refusals are specific — a name nothing accepts, or bytes that are not the
      // format the name claims — and each of them tells the uploader something they can act on.
      message.error(surveySourceProblemMessage(error, t));
    }
  };

  return (
    <Card
      title={t('surveySources.title')}
      style={{ marginTop: 16 }}
      extra={
        canEdit && (
          <Tooltip title={t('surveySources.acceptHint')}>
            <Upload
              accept={ACCEPTED_SOURCE_EXTENSIONS}
              showUploadList={false}
              beforeUpload={(file) => {
                void onUpload(file);
                return false;
              }}
              data-testid="survey-source-upload"
            >
              <Button size="small" icon={<UploadOutlined />} loading={upload.isPending}>
                {t('surveySources.upload')}
              </Button>
            </Upload>
          </Tooltip>
        )
      }
    >
      <Typography.Paragraph type="secondary" style={{ fontSize: 12 }}>
        {t('surveySources.explanation')}
      </Typography.Paragraph>
      <Table<SurveySourceInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        dataSource={sources}
        pagination={false}
        locale={{ emptyText: t('surveySources.empty') }}
        columns={[
          { title: t('caves.name'), dataIndex: 'name' },
          {
            title: t('surveySources.kind'),
            dataIndex: 'kind',
            width: 170,
            render: (kind: SurveySourceInfo['kind']) => <Tag>{t(KIND_LABEL_KEYS[kind])}</Tag>,
          },
          {
            title: t('surveySources.size'),
            dataIndex: 'sizeBytes',
            width: 110,
            align: 'right',
            render: (sizeBytes: number) => formatSize(sizeBytes),
          },
          {
            // A corrected source is a new revision of the same archived document rather than a
            // second row, so this number is the only sign in the list that one has been replaced.
            title: t('surveySources.revision'),
            dataIndex: 'versionNumber',
            width: 100,
            align: 'right',
          },
          {
            key: 'actions',
            width: 140,
            render: (_, source) => (
              <>
                <Button
                  size="small"
                  icon={<DownloadOutlined />}
                  href={source.contentUrl}
                  target="_blank"
                  rel="noreferrer"
                >
                  {t('surveySources.download')}
                </Button>
                {canEdit && (
                  <Popconfirm
                    title={t('surveySources.deleteConfirm')}
                    onConfirm={async () => {
                      try {
                        await remove.mutateAsync({ id: source.id, caveId });
                      } catch {
                        message.error(t('common.saveFailed'));
                      }
                    }}
                  >
                    <Button size="small" type="text" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                )}
              </>
            ),
          },
        ]}
      />
    </Card>
  );
}
