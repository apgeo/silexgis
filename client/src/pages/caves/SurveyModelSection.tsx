// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  CheckCircleOutlined,
  DeleteOutlined,
  ExportOutlined,
  EyeOutlined,
  PictureOutlined,
  ReloadOutlined,
  UploadOutlined,
  VideoCameraOutlined,
} from '@ant-design/icons';
import { App, Button, Card, Flex, Popconfirm, Table, Tag, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import {
  surveyModelReadableByViewer,
  surveyModelUnsettled,
  surveyModelWorkOutstanding,
  useDeleteSurveyModel,
  useMakeSurveyModelCurrent,
  useReadSurveyModelAgain,
  useSurveyModels,
  type SurveyModelInfo,
} from '../../api/hooks.ts';
import { formatSize } from '../../components/attachments/fileFormat.ts';
import SurveyModelViewerModal from '../../components/caveview/SurveyModelViewerModal.tsx';
import LazyTrackingMovieDialog from '../../components/caveview/movie/LazyTrackingMovieDialog.tsx';
import { openModelWindow } from '../../caveview/openModelWindow.ts';
import SurveyModelUploadModal from './SurveyModelUploadModal.tsx';
import { surveyModelProblemMessage } from './surveyModelProblems.ts';

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
  const navigate = useNavigate();
  const { data: models } = useSurveyModels(caveId);
  const remove = useDeleteSurveyModel();
  const makeCurrent = useMakeSurveyModelCurrent();
  const readAgain = useReadSurveyModelAgain();
  const [viewing, setViewing] = useState<SurveyModelInfo | null>(null);
  /** The model a movie is being made on, or null while the movie dialog is closed. */
  const [movieModelId, setMovieModelId] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);

  const stateCell = (model: SurveyModelInfo) => {
    // Both kinds of upload now have work waiting on them, and it is not the same work: walls are
    // converted into something the 3D scene can draw, while a line plot is read into its stations
    // and shots. The state names fit both; the sentence under them has to say which.
    const walls = model.format === 'stl';
    if (model.status === 'failed') {
      return (
        <Flex vertical gap={2}>
          <Tag color="error">{t('surveyModels.statusValues.failed')}</Tag>
          {/* The server's own words: it knows why this file could not be read, and no phrase
              written here in advance could say it as precisely. */}
          <Typography.Text type="danger" style={{ fontSize: 12 }}>
            {model.processingError ??
              t(walls ? 'surveyModels.conversionFailed' : 'surveyModels.readingFailed')}
          </Typography.Text>
        </Flex>
      );
    }
    if (surveyModelUnsettled(model.status)) {
      return (
        <Flex vertical gap={2}>
          <Tag color="processing">{t(`surveyModels.statusValues.${model.status}`)}</Tag>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t(walls ? 'surveyModels.converting' : 'surveyModels.reading')}
          </Typography.Text>
        </Flex>
      );
    }
    return (
      <Flex vertical gap={2}>
        <Tag color="success">{t('surveyModels.statusValues.ready')}</Tag>
        {/* Still ready, and said to be so: what the last reading produced is all in use until
            the one now queued replaces it. This only says that the row is about to change. */}
        {model.readingAgain && (
          <Tag color="processing" data-testid={`survey-model-reading-again-${model.id}`}>
            {t(walls ? 'surveyModels.convertingAgain' : 'surveyModels.readingAgain')}
          </Tag>
        )}
        {model.triangleCount !== null && (
          <Flex gap={6}>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('surveyModels.triangles', { count: model.triangleCount })}
            </Typography.Text>
            {/* The bytes the scene will download, which is what a viewer on a slow link wants
                to know; the triangle count alone says nothing about that. */}
            {model.meshSizeBytes !== null && (
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {formatSize(model.meshSizeBytes)}
              </Typography.Text>
            )}
          </Flex>
        )}
        {/* Triangles on a line plot are walls nobody uploaded: the reading built them. Said in
            words, because a mesh beside a plot otherwise looks like an export somebody made, and
            what it was made from decides how far it can be trusted — only what the survey
            itself measured or drew is in it. */}
        {!walls && model.triangleCount !== null && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t('surveyModels.wallsBuilt')}
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
          {
            title: t('caves.name'),
            dataIndex: 'name',
            render: (name: string, model) => (
              <Flex gap={6} align="center" wrap>
                <span>{name}</span>
                {/* One mark per kind: the line plot the map and the measurements read, and the
                    wall mesh the 3D scene draws. Shown to everybody, because a reader puzzled
                    by which of two plots the figures came from needs the answer as much as a
                    writer does. */}
                {model.isCurrent && <Tag color="blue">{t('surveyModels.current')}</Tag>}
              </Flex>
            ),
          },
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
            width: 320,
            render: (_, model) => (
              <Flex gap={4}>
                {surveyModelReadableByViewer(model) && (
                  <>
                    <Button size="small" icon={<EyeOutlined />} onClick={() => setViewing(model)}>
                      {t('surveyModels.view')}
                    </Button>
                    {/* The same model in the two other places it can be looked at. Behind the same
                        readability check as the overlay: a format the viewer cannot parse is no
                        more openable beside the map than it is over this page. */}
                    <Tooltip title={t('surveyModels.openBesideMap')}>
                      <Button
                        size="small"
                        type="text"
                        icon={<PictureOutlined />}
                        aria-label={t('surveyModels.openBesideMap')}
                        onClick={() => navigate(`/map?model=${model.id}`)}
                      />
                    </Tooltip>
                    <Tooltip title={t('surveyModels.openInWindow')}>
                      <Button
                        size="small"
                        type="text"
                        icon={<ExportOutlined />}
                        aria-label={t('surveyModels.openInWindow')}
                        onClick={() => openModelWindow(model.id)}
                      />
                    </Tooltip>
                    {/* A movie of the trips tracked on this model; which of them is chosen in the
                        dialog, since nothing on this page says which trip is meant. */}
                    <Tooltip title={t('caveview.movie.makeHelp')}>
                      <Button
                        size="small"
                        type="text"
                        icon={<VideoCameraOutlined />}
                        aria-label={t('caveview.movie.make')}
                        onClick={() => setMovieModelId(model.id)}
                        data-testid={`survey-model-movie-${model.id}`}
                      />
                    </Tooltip>
                  </>
                )}
                {/* Only a model that has finished can be chosen: for a line plot the choice hands
                    the map its centerline, and a model still being read has none to hand over. */}
                {canEdit && !model.isCurrent && model.status === 'ready' && (
                  <Button
                    size="small"
                    type="text"
                    icon={<CheckCircleOutlined />}
                    onClick={async () => {
                      try {
                        await makeCurrent.mutateAsync({ id: model.id, caveId });
                      } catch (error) {
                        message.error(
                          surveyModelProblemMessage(error, t, 'surveyModels.makeCurrentFailed'),
                        );
                      }
                    }}
                  >
                    {t('surveyModels.makeCurrent')}
                  </Button>
                )}
                {/* Another reading of the file that is already stored, for a model that has been
                    read or could not be. Not offered while a reading is queued or running —
                    the first one, or another one of a model that stays ready meanwhile: the
                    server would refuse it, and a second reading of the same bytes behind the
                    first would tell nobody anything. Asked about first, because it replaces
                    what the last reading produced. */}
                {canEdit && !surveyModelWorkOutstanding(model) && (
                  <Popconfirm
                    title={t('surveyModels.readAgainConfirm')}
                    description={
                      <div style={{ maxWidth: 360 }}>{t('surveyModels.readAgainHint')}</div>
                    }
                    onConfirm={async () => {
                      try {
                        await readAgain.mutateAsync({ id: model.id, caveId });
                        message.success(t('surveyModels.readAgainStarted'));
                      } catch (error) {
                        message.error(
                          surveyModelProblemMessage(error, t, 'surveyModels.readAgainFailed'),
                        );
                      }
                    }}
                  >
                    <Tooltip title={t('surveyModels.readAgain')}>
                      <Button
                        size="small"
                        type="text"
                        icon={<ReloadOutlined />}
                        aria-label={t('surveyModels.readAgain')}
                        data-testid={`survey-model-read-again-${model.id}`}
                      />
                    </Tooltip>
                  </Popconfirm>
                )}
                {canEdit && (
                  <Popconfirm
                    title={t('surveyModels.deleteConfirm')}
                    onConfirm={async () => {
                      try {
                        await remove.mutateAsync({ id: model.id, caveId });
                      } catch (error) {
                        // The server's own reason, where it has one. This delete can be refused
                        // because a trip's live tracking is armed on the model, and a person told
                        // only "could not be saved" would try again, then wonder — while the thing
                        // they have to do instead is on another page entirely.
                        message.error(
                          surveyModelProblemMessage(error, t, 'surveyModels.deleteFailed'),
                        );
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
      {(models?.length ?? 0) > 0 && (
        <Typography.Paragraph
          type="secondary"
          style={{ fontSize: 12, marginTop: 8, marginBottom: 0 }}
        >
          {t('surveyModels.currentHint')}
        </Typography.Paragraph>
      )}

      {canEdit && (
        <SurveyModelUploadModal
          caveId={caveId}
          open={uploading}
          onClose={() => setUploading(false)}
        />
      )}

      <SurveyModelViewerModal model={viewing} onClose={() => setViewing(null)} />
      <LazyTrackingMovieDialog surveyModelId={movieModelId} onClose={() => setMovieModelId(null)} />
    </Card>
  );
}
