// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Card, Descriptions, Flex, Radio, Table, Tag, Typography } from 'antd';
import { EyeOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import PointCloudViewerModal from '../scene3d/PointCloudViewerModal.tsx';
import { formatSize } from '../attachments/fileFormat.ts';
import StorageRoomSummary from '../storage/StorageRoomSummary.tsx';
import { ExternalStepPanel, RefineOnDesktopPanel } from './ScanExternalSteps.tsx';
import ScanPictures from './ScanPictures.tsx';
import ScanSurfacePanel from './ScanSurfacePanel.tsx';
import { formatBytes, originalBytesOf, runAddsBytes } from '../storage/storageFigures.ts';
import {
  displayChoiceUsed,
  pointCloudChoiceNeeded,
  suggestedDisplayChoice,
  surveyUploadAwaitingExternal,
  type PointCloudAdviceLevel,
  type PointCloudDisplayChoice,
  type SurveyUpload,
  type SurveyUploadPointCloud,
  type SurveyUploadPointCloudEstimateRow,
  type SurveyUploadRecording,
  useSurveyModelCentreline,
} from '../../api/hooks.ts';

/** One row of the estimate table, keyed so a radio can name it. */
interface EstimateRow extends SurveyUploadPointCloudEstimateRow {
  key: string;
  choice: PointCloudDisplayChoice;
  suggested: boolean;
}

function rowKey(choice: PointCloudDisplayChoice): string {
  return choice.method === 'full' ? 'full' : `voxel-${choice.voxelCm}`;
}

function sameChoice(a: PointCloudDisplayChoice, b: PointCloudDisplayChoice): boolean {
  return a.method === b.method && (a.method === 'full' || b.method === 'full' || a.voxelCm === b.voxelCm);
}

/** The seconds the server estimated, said in minutes once there are enough of them to matter. */
function formatSeconds(seconds: number, t: (key: string, options?: Record<string, unknown>) => string): string {
  return seconds < 90
    ? t('surveyUploads.pointCloud.seconds', { count: Math.max(1, Math.round(seconds)) })
    : t('surveyUploads.pointCloud.minutes', { count: Math.round(seconds / 60) });
}

/**
 * The scan's card: what the header said about the cloud, what the run made of it, and — when the
 * run stopped to ask — the table of how much would remain at each voxel size, with the
 * installation's suggestion pre-selected and the person's choice sent back as a rerun.
 *
 * Everything on the card is allowed by the default survey-file rules except what the server
 * already withholds: the coordinate system and the extent of a georeferenced cloud come only to a
 * reader with the exact position, and the card draws what it was sent. The table is offered to
 * somebody who may write the cave, because the choice starts a run; a reader sees the figures and
 * the suggestion, and that the run is waiting for a choice.
 */
export default function PointCloudCard({
  upload,
  name,
  canWrite,
  onChoose,
  choosing = false,
}: {
  upload: Pick<SurveyUpload, 'status' | 'stage' | 'pointCloud' | 'files'> &
    Partial<Pick<SurveyUpload, 'id' | 'cave' | 'caveSurvey' | 'external'>>;
  /** What the viewer is titled with; the survey's title where the card has one. */
  name?: string;
  canWrite: boolean;
  /** Sends the choice as a rerun with a display request. */
  onChoose?: (choice: PointCloudDisplayChoice) => void | Promise<void>;
  choosing?: boolean;
}) {
  const { t, i18n } = useTranslation();
  const [viewing, setViewing] = useState(false);
  const cloud = upload.pointCloud;
  if (!cloud) {
    return null;
  }

  const choiceNeeded = pointCloudChoiceNeeded(upload);
  const awaiting = surveyUploadAwaitingExternal(upload);
  // Refining on a desktop starts from a run that finished, with the session it made (or was sent)
  // and a survey to send the refined one to as its next version. The server sends the session's
  // address only with the exact location; a run still waiting has nothing to refine yet.
  const refinable =
    canWrite && upload.status === 'succeeded' && !awaiting && !!cloud.sessionUrl && !!upload.cave && !!upload.caveSurvey;
  // The uploaded original carries real coordinates when its header names a coordinate system, so
  // the server offers its link only with the exact position; the card says why the link is gone
  // rather than leaving a row that silently lacks a button.
  const originalWithheld =
    cloud.georeferenced && upload.files.some((file) => file.role === 'archive' && file.contentUrl === null);

  return (
    <Card
      size="small"
      title={t('surveyUploads.pointCloud.title')}
      data-testid="point-cloud-card"
      extra={
        // The tiles carry no coordinates, so the scan is viewable by whoever may read this card,
        // placed or not — the viewer needs only that the tiling has produced a tileset.
        cloud.tilesetUrl !== null && (
          <Button size="small" icon={<EyeOutlined />} onClick={() => setViewing(true)} data-testid="point-cloud-view">
            {t('surveyUploads.pointCloud.view')}
          </Button>
        )
      }
    >
      <Flex vertical gap={12}>
        {awaiting && canWrite && upload.id && upload.external && (
          <ExternalStepPanel id={upload.id} external={upload.external} />
        )}
        {awaiting && !(canWrite && upload.external) && (
          <Typography.Text type="secondary" data-testid="scan-external-wait-reader">
            {t('surveyUploads.external.waitingReader')}
          </Typography.Text>
        )}
        <Facts cloud={cloud} language={i18n.language} />
        {cloud.recording && <RecordingFacts recording={cloud.recording} language={i18n.language} />}
        {originalWithheld && (
          <Typography.Text type="secondary" data-testid="point-cloud-original-withheld">
            {t('surveyUploads.pointCloud.originalWithheld')}
          </Typography.Text>
        )}
        {cloud.estimate && (
          <EstimateTable
            cloud={cloud}
            originalBytes={originalBytesOf(upload.files)}
            choiceNeeded={choiceNeeded}
            canChoose={canWrite && upload.status === 'succeeded' && onChoose !== undefined}
            onChoose={onChoose}
            choosing={choosing}
          />
        )}
        {cloud.pictures && <ScanPictures pictures={cloud.pictures} />}
        {/* Making a surface writes a wall mesh of a cave, so it is offered only beside a run the
            caller may start; the card on the cave page is drawn read-only and never offers it. */}
        {canWrite && upload.id && upload.cave && !awaiting && (
          <ScanSurfacePanel upload={{ id: upload.id, status: upload.status, cave: upload.cave, pointCloud: cloud }} />
        )}
        {refinable && upload.cave && upload.caveSurvey && (
          <RefineOnDesktopPanel upload={{ cave: upload.cave, caveSurvey: upload.caveSurvey }} sessionUrl={cloud.sessionUrl!} />
        )}
      </Flex>
      {viewing && cloud.tilesetUrl !== null && (
        <CardScanViewer
          cloud={cloud}
          tilesetUrl={cloud.tilesetUrl}
          name={name ?? t('surveyUploads.pointCloud.title')}
          onClose={() => setViewing(false)}
        />
      )}
    </Card>
  );
}

/**
 * The scan viewer as the card opens it. Mounted only while open, so the centreline is read only
 * when somebody looks: it is drawn in the viewer and nowhere on the card. The server says a scan
 * has one only to a reader with the exact location, since the line is the shape of the cave the
 * scanner went through.
 */
function CardScanViewer({
  cloud,
  tilesetUrl,
  name,
  onClose,
}: {
  cloud: SurveyUploadPointCloud;
  tilesetUrl: string;
  name: string;
  onClose(): void;
}) {
  const centreline = useSurveyModelCentreline(cloud.modelId, cloud.hasCentreline);
  return (
    <PointCloudViewerModal
      open
      tilesetUrl={tilesetUrl}
      name={name}
      hasRgb={cloud.hasRgb}
      hasIntensity={cloud.hasIntensity === true}
      onClose={onClose}
      centreline={centreline.data?.coordinates.map(([x = 0, y = 0, z = 0]) => ({ x, y, z }))}
    />
  );
}

function Facts({ cloud, language }: { cloud: SurveyUploadPointCloud; language: string }) {
  const { t } = useTranslation();
  const number = (value: number) => value.toLocaleString(language);
  const metres = (value: number) => t('surveyUploads.pointCloud.metres', { value: number(Math.round(value * 10) / 10) });
  const used = displayChoiceUsed(cloud);
  const [sizeX, sizeY, sizeZ] = cloud.sizeM;

  return (
    <Descriptions size="small" column={{ xs: 1, sm: 2 }} data-testid="point-cloud-facts">
      <Descriptions.Item label={t('surveyUploads.pointCloud.points')}>{number(cloud.pointCount)}</Descriptions.Item>
      <Descriptions.Item label={t('surveyUploads.pointCloud.format')}>
        {t(cloud.compressed ? 'surveyUploads.pointCloud.formatLaz' : 'surveyUploads.pointCloud.formatLas', {
          version: cloud.lasVersion,
          pointFormat: cloud.pointFormat,
        })}
      </Descriptions.Item>
      <Descriptions.Item label={t('surveyUploads.pointCloud.attributes')}>
        <Flex gap={4} wrap>
          {cloud.hasRgb && <Tag>{t('surveyUploads.pointCloud.rgb')}</Tag>}
          {cloud.hasIntensity === true && <Tag>{t('surveyUploads.pointCloud.intensity')}</Tag>}
          {!cloud.hasRgb && cloud.hasIntensity !== true && (
            <Typography.Text type="secondary">{t('surveyUploads.pointCloud.positionsOnly')}</Typography.Text>
          )}
        </Flex>
      </Descriptions.Item>
      {sizeX !== undefined && sizeY !== undefined && sizeZ !== undefined && (
        <Descriptions.Item label={t('surveyUploads.pointCloud.extent')}>
          {`${metres(sizeX)} × ${metres(sizeY)} × ${metres(sizeZ)}`}
        </Descriptions.Item>
      )}
      <Descriptions.Item label={t('surveyUploads.pointCloud.coordinates')}>
        {cloud.georeferenced ? (
          <Flex gap={4} wrap align="center">
            <Tag color="blue" data-testid="point-cloud-georeferenced">{t('surveyUploads.pointCloud.georeferenced')}</Tag>
            {cloud.crs?.horizontalEpsg !== null && cloud.crs?.horizontalEpsg !== undefined && (
              <Typography.Text>EPSG:{cloud.crs.horizontalEpsg}</Typography.Text>
            )}
            {cloud.crs?.horizontalEpsg === null && cloud.crs.wkt && (
              <Typography.Text type="secondary">{t('surveyUploads.pointCloud.crsWkt')}</Typography.Text>
            )}
          </Flex>
        ) : (
          <Typography.Text type="secondary">{t('surveyUploads.pointCloud.local')}</Typography.Text>
        )}
      </Descriptions.Item>
      {cloud.generatingSoftware && (
        <Descriptions.Item label={t('surveyUploads.pointCloud.software')}>{cloud.generatingSoftware}</Descriptions.Item>
      )}
      <Descriptions.Item label={t('surveyUploads.pointCloud.tiles')}>
        {cloud.tileBytes !== null && cloud.tileCount !== null ? (
          <span data-testid="point-cloud-tiles">
            {t('surveyUploads.pointCloud.tiled', { size: formatSize(cloud.tileBytes), count: cloud.tileCount })}
            {used && (
              <Typography.Text type="secondary" style={{ marginInlineStart: 8 }}>
                {used.method === 'full'
                  ? t('surveyUploads.pointCloud.shownFull')
                  : t('surveyUploads.pointCloud.shownVoxel', {
                      voxelCm: number(used.voxelCm),
                      points: cloud.displayPointCount === null ? '' : number(cloud.displayPointCount),
                    })}
              </Typography.Text>
            )}
          </span>
        ) : (
          <Typography.Text type="secondary" data-testid="point-cloud-not-tiled">
            {t('surveyUploads.pointCloud.notTiled')}
          </Typography.Text>
        )}
      </Descriptions.Item>
    </Descriptions>
  );
}

/**
 * What reading a raw recording found: how many chunks and sensors, whether it carries a
 * calibration and a GNSS log, how long it ran and how large it is. Measurements only — a GNSS log
 * is said to be there, never where it was — so the block goes to whoever may read the card.
 */
function RecordingFacts({ recording, language }: { recording: SurveyUploadRecording; language: string }) {
  const { t } = useTranslation();
  const number = (value: number) => value.toLocaleString(language);
  const yesNo = (value: boolean) => t(value ? 'surveyUploads.recording.yes' : 'surveyUploads.recording.no');
  const duration = recording.durationSeconds;

  return (
    <Descriptions
      size="small"
      column={{ xs: 1, sm: 2 }}
      title={t('surveyUploads.recording.factsTitle')}
      data-testid="point-cloud-recording"
    >
      <Descriptions.Item label={t('surveyUploads.recording.chunks')}>
        <span data-testid="point-cloud-recording-chunks">{number(recording.chunkCount)}</span>
      </Descriptions.Item>
      <Descriptions.Item label={t('surveyUploads.recording.sensors')}>
        {recording.sensorCount === null ? t('surveyUploads.recording.unknown') : number(recording.sensorCount)}
      </Descriptions.Item>
      <Descriptions.Item label={t('surveyUploads.recording.duration')}>
        {duration === null ? t('surveyUploads.recording.unknown') : formatSeconds(duration, t)}
      </Descriptions.Item>
      <Descriptions.Item label={t('surveyUploads.recording.size')}>{formatBytes(recording.bytes)}</Descriptions.Item>
      <Descriptions.Item label={t('surveyUploads.recording.calibration')}>{yesNo(recording.hasCalibration)}</Descriptions.Item>
      <Descriptions.Item label={t('surveyUploads.recording.gnss')}>
        <span data-testid="point-cloud-recording-gnss">{yesNo(recording.hasGnss)}</span>
      </Descriptions.Item>
    </Descriptions>
  );
}

/**
 * How many points would remain at each voxel size, beside the full cloud, with the tile size and
 * the time each would take. The suggestion is the smallest voxel that fits the installation's
 * thresholds, and it is the row selected until the person picks another.
 */
function EstimateTable({
  cloud,
  originalBytes,
  choiceNeeded,
  canChoose,
  onChoose,
  choosing,
}: {
  cloud: SurveyUploadPointCloud;
  originalBytes: number | null;
  choiceNeeded: boolean;
  canChoose: boolean;
  onChoose?: (choice: PointCloudDisplayChoice) => void | Promise<void>;
  choosing: boolean;
}) {
  const { t, i18n } = useTranslation();
  const estimate = cloud.estimate!;
  const suggested = suggestedDisplayChoice(cloud);
  const used = displayChoiceUsed(cloud);
  const [selected, setSelected] = useState<PointCloudDisplayChoice>(used ?? suggested);
  const number = (value: number) => value.toLocaleString(i18n.language);
  const level = (cloud.advice?.level ?? 'none') as PointCloudAdviceLevel;

  const rows: EstimateRow[] = [
    { ...estimate.full, key: 'full', choice: { method: 'full' }, suggested: sameChoice(suggested, { method: 'full' }) },
    ...estimate.voxels
      .filter((row) => row.voxelCm !== null)
      .map((row) => {
        const choice: PointCloudDisplayChoice = { method: 'voxel', voxelCm: row.voxelCm! };
        return { ...row, key: rowKey(choice), choice, suggested: sameChoice(suggested, choice) };
      }),
  ];

  return (
    <Flex vertical gap={8} data-testid="point-cloud-estimate">
      <Typography.Text strong>{t('surveyUploads.pointCloud.estimateTitle')}</Typography.Text>
      {level !== 'none' && cloud.advice && (
        <Alert
          type={level === 'advise' ? 'warning' : 'info'}
          showIcon
          data-testid="point-cloud-advice"
          title={t(`surveyUploads.pointCloud.advice.${level}`, {
            points: number(cloud.pointCount),
            warnPoints: number(cloud.advice.warnPoints),
            advisePoints: number(cloud.advice.advisePoints),
            warnTiles: formatSize(cloud.advice.warnTileBytes),
          })}
          description={
            suggested.method === 'voxel'
              ? t('surveyUploads.pointCloud.suggestion', { voxelCm: number(suggested.voxelCm) })
              : undefined
          }
        />
      )}
      <Radio.Group
        value={rowKey(selected)}
        onChange={(event) => {
          const row = rows.find((candidate) => candidate.key === event.target.value);
          if (row) {
            setSelected(row.choice);
          }
        }}
        disabled={!canChoose}
        style={{ display: 'block' }}
      >
        <Table<EstimateRow>
          size="small"
          rowKey="key"
          pagination={false}
          scroll={{ x: 'max-content' }}
          dataSource={rows}
          onRow={(row) => ({ 'data-testid': `point-cloud-estimate-row-${row.key}` }) as never}
          columns={[
            {
              title: t('surveyUploads.pointCloud.detail'),
              render: (_, row) => (
                <Radio value={row.key} data-testid={`point-cloud-choice-${row.key}`}>
                  {row.choice.method === 'full'
                    ? t('surveyUploads.pointCloud.fullCloud')
                    : t('surveyUploads.pointCloud.voxelRow', { voxelCm: number(row.choice.voxelCm) })}
                  {row.suggested && (
                    <Tag color="blue" style={{ marginInlineStart: 8 }} data-testid="point-cloud-suggested">
                      {t('surveyUploads.pointCloud.suggested')}
                    </Tag>
                  )}
                  {used && sameChoice(used, row.choice) && (
                    <Tag style={{ marginInlineStart: 8 }}>{t('surveyUploads.pointCloud.inUse')}</Tag>
                  )}
                </Radio>
              ),
            },
            {
              title: t('surveyUploads.pointCloud.points'),
              align: 'right',
              render: (_, row) => (row.points === null ? '—' : number(row.points)),
            },
            {
              title: t('surveyUploads.pointCloud.estimatedTiles'),
              align: 'right',
              render: (_, row) => (row.estimatedTileBytes === null ? '—' : formatSize(row.estimatedTileBytes)),
            },
            {
              title: t('surveyUploads.pointCloud.estimatedTime'),
              align: 'right',
              render: (_, row) => (row.estimatedSeconds === null ? '—' : formatSeconds(row.estimatedSeconds, t)),
            },
          ]}
        />
      </Radio.Group>
      {choiceNeeded && (
        <Typography.Text type="secondary" data-testid="point-cloud-choice-needed">
          {t('surveyUploads.pointCloud.choiceNeeded')}
        </Typography.Text>
      )}
      {canChoose && (
        // What the run with the chosen row would add — the thinned copy and the tiles, as the server
        // counts them — against the totals and the worker's disk, so a run that will not fit is
        // seen before it is started rather than refused after.
        <StorageRoomSummary
          addingBytes={runAddsBytes(
            selected.method === 'voxel',
            originalBytes,
            rows.find((row) => row.key === rowKey(selected))?.estimatedTileBytes,
          )}
          scan
          processing
        />
      )}
      {canChoose && (
        <Flex>
          <Button
            type={choiceNeeded ? 'primary' : 'default'}
            loading={choosing}
            onClick={() => void onChoose?.(selected)}
            data-testid="point-cloud-choose"
          >
            {t(choiceNeeded ? 'surveyUploads.pointCloud.tileWithChoice' : 'surveyUploads.pointCloud.tileAgainWithChoice')}
          </Button>
        </Flex>
      )}
    </Flex>
  );
}
