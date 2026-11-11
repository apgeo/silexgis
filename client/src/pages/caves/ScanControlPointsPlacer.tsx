// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { Alert, App, Checkbox, Flex, Table, Tag, Typography } from 'antd';
import { AimOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import Button from '../../components/Button.tsx';
import SurveyStationInput from '../../components/caves/SurveyStationInput.tsx';
import PointCloudViewerModal from '../../components/scene3d/PointCloudViewerModal.tsx';
import type { Scene3DScanPoint } from '../../scene3d/scene3dEngine.ts';
import {
  usePlaceSurveyModelByControlPoints,
  useSurveyModels,
  type SurveyModelControlPoint,
  type SurveyModelInfo,
} from '../../api/hooks.ts';
import { surveyModelProblemMessage } from './surveyModelProblems.ts';

/** The fewest pairs a placement is fitted from, and the fewest a tilt is: the server's own limits. */
const MINIMUM_PAIRS = 2;
const MINIMUM_TILT_PAIRS = 3;

interface Pair {
  key: number;
  point?: Scene3DScanPoint;
  station: string;
}

function metres(value: number, language: string): string {
  return new Intl.NumberFormat(language, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);
}

/**
 * Placing a scan by control points: the scan opened in its own frame with a panel beside it, where
 * each pair is a point clicked on the cloud and the station of the cave's current line plot it
 * stands on. Saving sends the pairs; the server fits the translation and heading (and a small tilt
 * when asked) and answers each pair's residual, which the panel shows with the pairs over the
 * installation's warning distance flagged — usually a point paired with the wrong station.
 *
 * Opened only by somebody who may write the cave and see its exact position: the stations'
 * positions and the residuals are where the cave is. A scan placed this way before opens with its
 * pairs, so one bad pair can be changed or dropped and the rest sent again — placing never touches
 * the tiles, so each try costs one request.
 */
export default function ScanControlPointsPlacer({
  model,
  caveId,
  open,
  onClose,
}: {
  model: SurveyModelInfo & { tilesetUrl: string };
  caveId: string;
  open: boolean;
  onClose(): void;
}) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data: models } = useSurveyModels(caveId);
  const place = usePlaceSurveyModelByControlPoints();
  // The stations are the cave's current line plot's — the survey the server fits against.
  const linePlot = models?.find((m) => m.isCurrent && (m.format === 'lox' || m.format === 'survex3d'));

  const initial = model.placementControlPoints?.pairs ?? [];
  const [pairs, setPairs] = useState<Pair[]>(() =>
    initial.length > 0
      ? initial.map((p, index) => ({ key: index, point: { x: p.scanX, y: p.scanY, z: p.scanZ }, station: p.station }))
      : [
          { key: 0, station: '' },
          { key: 1, station: '' },
        ],
  );
  const [nextKey, setNextKey] = useState(Math.max(2, initial.length));
  const [pickingFor, setPickingFor] = useState<number | null>(null);
  const [fitTilt, setFitTilt] = useState(model.placementControlPoints?.tiltFitted === true);
  const [result, setResult] = useState(model.placementControlPoints);

  const marks = useMemo(() => pairs.flatMap((pair) => (pair.point ? [pair.point] : [])), [pairs]);
  const complete = pairs.filter((pair) => pair.point && pair.station.trim().length > 0);
  const canSave = complete.length >= MINIMUM_PAIRS && (!fitTilt || complete.length >= MINIMUM_TILT_PAIRS);

  const update = (key: number, change: Partial<Pair>) =>
    setPairs((current) => current.map((pair) => (pair.key === key ? { ...pair, ...change } : pair)));

  const save = async () => {
    try {
      const placed = await place.mutateAsync({
        id: model.id,
        caveId,
        request: {
          pairs: complete.map((pair) => ({
            scanX: pair.point!.x,
            scanY: pair.point!.y,
            scanZ: pair.point!.z,
            station: pair.station.trim(),
          })),
          fitTilt,
        },
      });
      setResult(placed.placementControlPoints);
      message.success(t('surveyModels.controlPoints.placed'));
    } catch (error) {
      message.error(surveyModelProblemMessage(error, t, 'surveyModels.scanPlacement.failed'));
    }
  };

  const panel = (
    <Flex vertical gap={10} data-testid="scan-control-points">
      <Typography.Text type="secondary">{t('surveyModels.controlPoints.hint')}</Typography.Text>
      {models !== undefined && linePlot === undefined && (
        <Alert type="warning" showIcon title={t('surveyModels.controlPoints.noLinePlot')} />
      )}
      {pairs.map((pair, index) => (
        <Flex key={pair.key} vertical gap={4} data-testid={`scan-control-pair-${index}`}>
          <Flex gap={6} align="center" wrap>
            <Typography.Text strong>{t('surveyModels.controlPoints.pair', { number: index + 1 })}</Typography.Text>
            <Typography.Text type="secondary" data-testid={`scan-control-point-${index}`}>
              {pair.point
                ? t('surveyModels.controlPoints.point', {
                    x: metres(pair.point.x, i18n.language),
                    y: metres(pair.point.y, i18n.language),
                    z: metres(pair.point.z, i18n.language),
                  })
                : t('surveyModels.controlPoints.notPicked')}
            </Typography.Text>
          </Flex>
          <Flex gap={6} align="center">
            <Button
              size="small"
              icon={<AimOutlined />}
              type={pickingFor === pair.key ? 'primary' : 'default'}
              aria-pressed={pickingFor === pair.key}
              onClick={() => setPickingFor(pickingFor === pair.key ? null : pair.key)}
              data-testid={`scan-control-pick-${index}`}
            >
              {t(pickingFor === pair.key ? 'surveyModels.controlPoints.picking' : 'surveyModels.controlPoints.pick')}
            </Button>
            <SurveyStationInput
              surveyModelId={linePlot?.id}
              value={pair.station}
              onChange={(station) => update(pair.key, { station })}
              style={{ flex: 1, minWidth: 0 }}
              aria-required
              data-testid={`scan-control-station-${index}`}
            />
            <Button
              size="small"
              type="text"
              icon={<DeleteOutlined />}
              aria-label={t('surveyModels.controlPoints.removePair')}
              disabled={pairs.length <= MINIMUM_PAIRS}
              onClick={() => {
                setPairs((current) => current.filter((p) => p.key !== pair.key));
                if (pickingFor === pair.key) setPickingFor(null);
              }}
            />
          </Flex>
        </Flex>
      ))}
      <Flex gap={8} align="center" wrap>
        <Button
          size="small"
          icon={<PlusOutlined />}
          onClick={() => {
            setPairs((current) => [...current, { key: nextKey, station: '' }]);
            setNextKey(nextKey + 1);
          }}
          data-testid="scan-control-add"
        >
          {t('surveyModels.controlPoints.addPair')}
        </Button>
        <Checkbox checked={fitTilt} onChange={(event) => setFitTilt(event.target.checked)} data-testid="scan-control-tilt">
          {t('surveyModels.controlPoints.fitTilt')}
        </Checkbox>
      </Flex>
      {fitTilt && complete.length < MINIMUM_TILT_PAIRS && (
        <Typography.Text type="secondary">
          {t('surveyModels.controlPoints.tiltNeedsThree', { count: MINIMUM_TILT_PAIRS })}
        </Typography.Text>
      )}
      <Button
        type="primary"
        disabled={!canSave || linePlot === undefined}
        loading={place.isPending}
        onClick={() => void save()}
        data-testid="scan-control-save"
      >
        {t('surveyModels.controlPoints.save')}
      </Button>
      {result && <Residuals result={result} language={i18n.language} />}
    </Flex>
  );

  return (
    <PointCloudViewerModal
      open={open}
      tilesetUrl={model.tilesetUrl}
      name={model.name}
      hasRgb={false}
      hasIntensity={false}
      onClose={onClose}
      picking={{
        active: pickingFor !== null,
        marks,
        onPick: (point) => {
          if (pickingFor === null) return;
          update(pickingFor, { point });
          setPickingFor(null);
        },
      }}
      aside={panel}
    />
  );
}

/** What the fit said about each pair: how far its point ended up from its station, and the RMS. */
function Residuals({
  result,
  language,
}: {
  result: NonNullable<SurveyModelInfo['placementControlPoints']>;
  language: string;
}) {
  const { t } = useTranslation();
  const flagged = result.pairs.filter((pair) => pair.flagged).length;
  return (
    <Flex vertical gap={6} data-testid="scan-control-residuals">
      <Typography.Text strong data-testid="scan-control-rms">
        {t('surveyModels.controlPoints.rms', { metres: metres(result.rmsM, language) })}
      </Typography.Text>
      {flagged > 0 && (
        <Alert
          type="warning"
          showIcon
          title={t('surveyModels.controlPoints.flagged', { count: flagged, metres: metres(result.warnM, language) })}
        />
      )}
      <Table<SurveyModelControlPoint & { key: number }>
        size="small"
        pagination={false}
        dataSource={result.pairs.map((pair, index) => ({ ...pair, key: index }))}
        columns={[
          { title: t('surveyModels.controlPoints.station'), dataIndex: 'station' },
          {
            title: t('surveyModels.controlPoints.horizontal'),
            dataIndex: 'horizontalM',
            align: 'right',
            render: (value: number) => metres(value, language),
          },
          {
            title: t('surveyModels.controlPoints.vertical'),
            dataIndex: 'upM',
            align: 'right',
            render: (value: number) => metres(value, language),
          },
          {
            title: t('surveyModels.controlPoints.distance'),
            key: 'distance',
            align: 'right',
            render: (_, pair) =>
              pair.flagged ? (
                <Tag color="red" data-testid="scan-control-flagged">
                  {metres(pair.distanceM, language)}
                </Tag>
              ) : (
                metres(pair.distanceM, language)
              ),
          },
        ]}
      />
      {result.tiltFitted && <Typography.Text type="secondary">{t('surveyModels.controlPoints.tiltFitted')}</Typography.Text>}
    </Flex>
  );
}
