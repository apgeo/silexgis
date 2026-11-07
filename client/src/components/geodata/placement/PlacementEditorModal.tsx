// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { AimOutlined } from '@ant-design/icons';
import { Alert, App, Collapse, Flex, Modal, Popconfirm, Segmented, Slider, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../../api/client.ts';
import {
  useClearRasterPlacement,
  usePreviewRasterPlacement,
  useSaveRasterPlacement,
  type RasterMapInfo,
  type RasterMapPlacementPreview,
} from '../../../api/hooks.ts';
import { placementErrorKey } from '../../../geodata/placement/errors.ts';
import {
  CORNER_IDS,
  cornersOfPlacement,
  cornersToPlacement,
  cornerTransformationOf,
  formatMetres,
  initialCorners,
  isUsableQuadrilateral,
  pairsToPlacement,
  previewImageOptions,
  sheetAspect,
  type CornerTransformation,
  type Corners,
  type PlacementBody,
  type PointPair,
} from '../../../geodata/placement/placement.ts';
import { toLonLat } from 'ol/proj';
import Button from '../../Button.tsx';
import ControlPointTable from './ControlPointTable.tsx';
import { CORNER_LABEL_KEYS } from './cornerLabels.ts';
import CornerTable from './CornerTable.tsx';
import PlacementMap from './PlacementMap.tsx';
import SheetPicker from './SheetPicker.tsx';

type Mode = 'corners' | 'points';

/** How long after the last change the preview is asked for, so a burst of edits asks once. */
const PREVIEW_DELAY_MS = 150;

interface Props {
  /** The sheet being placed; null while the dialog is closed. */
  raster: RasterMapInfo | null;
  onClose: () => void;
}

/**
 * Lays a drawn map sheet on the map: by its four corners, or by two points matched between the
 * sheet and the ground. Every change asks the server for a preview of the sheet where the points
 * put it, drawn over the map; Save keeps the placement, and the server then publishes the sheet
 * in the background.
 */
export default function PlacementEditorModal({ raster, onClose }: Props) {
  const { t } = useTranslation();
  const [active, setActive] = useState(false);
  return (
    <Modal
      open={raster !== null}
      onCancel={onClose}
      afterOpenChange={setActive}
      footer={null}
      width="min(1200px, 96vw)"
      destroyOnHidden
      title={raster ? t('geodata.placement.title', { name: raster.name }) : undefined}
      data-testid="placement-editor"
    >
      {raster && <PlacementEditor key={raster.id} raster={raster} onClose={onClose} active={active} />}
    </Modal>
  );
}

function codeOf(error: unknown): string | undefined {
  return error instanceof ApiError ? error.code : undefined;
}

function pairsOfPlacement(placement: PlacementBody | null | undefined): PointPair[] {
  if (!placement || cornersOfPlacement(placement) !== null || placement.crs !== 'EPSG:4326') {
    return [];
  }
  return placement.gcps.map((p) => ({ u: p.u, v: p.v, x: p.x, y: p.y }));
}

function PlacementEditor({ raster, onClose, active }: { raster: RasterMapInfo; onClose: () => void; active: boolean }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const preview = usePreviewRasterPlacement();
  const save = useSaveRasterPlacement();
  const clear = useClearRasterPlacement();

  const existing = raster.placement ?? null;
  const [corners, setCorners] = useState<Corners | null>(() => cornersOfPlacement(existing));
  const [pairs, setPairs] = useState<PointPair[]>(() => pairsOfPlacement(existing));
  const [mode, setMode] = useState<Mode>(() =>
    existing && cornersOfPlacement(existing) === null && pairsOfPlacement(existing).length > 0 ? 'points' : 'corners',
  );
  const [transformation, setTransformation] = useState<CornerTransformation>(() => cornerTransformationOf(existing));
  const [picking, setPicking] = useState<'sheet' | 'map' | null>(null);
  const [opacity, setOpacity] = useState(0.7);
  const [answer, setAnswer] = useState<RasterMapPlacementPreview | null>(null);
  const [previewState, setPreviewState] = useState<'idle' | 'pending' | 'ready' | 'error'>('idle');
  const [previewError, setPreviewError] = useState<string | null>(null);

  const body = useMemo<PlacementBody | null>(() => {
    if (mode === 'corners') {
      return corners && isUsableQuadrilateral(corners) ? cornersToPlacement(corners, transformation) : null;
    }
    return pairsToPlacement(pairs, 'similarity');
  }, [mode, corners, transformation, pairs]);
  const bodyKey = body === null ? null : JSON.stringify(body);

  // The newest preview request; a newer arrangement of the points aborts the one before it.
  const askPreview = useRef(preview.mutateAsync);
  askPreview.current = preview.mutateAsync;
  const inFlight = useRef<AbortController | null>(null);
  useEffect(() => {
    inFlight.current?.abort();
    inFlight.current = null;
    if (body === null) {
      setPreviewState('idle');
      return undefined;
    }
    // Pending from the change itself, not from when the request leaves: the answer on screen is
    // already out of date, and whoever waits for "ready" must not take the old one for the new.
    setPreviewState('pending');
    const timer = window.setTimeout(() => {
      const controller = new AbortController();
      inFlight.current = controller;
      askPreview
        .current({ id: raster.id, body, signal: controller.signal })
        .then((result) => {
          if (controller.signal.aborted) {
            return;
          }
          setAnswer(result);
          setPreviewError(null);
          setPreviewState('ready');
        })
        .catch((error: unknown) => {
          if (controller.signal.aborted) {
            return;
          }
          setPreviewError(t(placementErrorKey(codeOf(error))));
          setPreviewState('error');
        });
    }, PREVIEW_DELAY_MS);
    return () => window.clearTimeout(timer);
    // The key stands for the body: a new object with the same contents asks nothing new.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bodyKey, raster.id]);
  useEffect(() => () => inFlight.current?.abort(), []);

  const image = useMemo(() => (answer ? previewImageOptions(answer) : null), [answer]);

  const onViewReady = (extent3857: [number, number, number, number]) => {
    if (corners !== null) {
      return;
    }
    const planar = initialCorners(extent3857, sheetAspect(raster.renderWidth, raster.renderHeight));
    const lonLat = Object.fromEntries(
      CORNER_IDS.map((id) => [id, toLonLat(planar[id]) as [number, number]]),
    ) as Corners;
    setCorners(lonLat);
  };

  const onMapClick =
    mode === 'points' && picking === 'map'
      ? ([x, y]: [number, number]) => {
          const last = pairs.length - 1;
          if (last < 0 || pairs[last].x !== null) {
            return;
          }
          setPairs(pairs.map((pair, index) => (index === last ? { ...pair, x, y } : pair)));
          // Two pairs make a similarity; picking stops there, and "Add a point" starts another.
          setPicking(pairs.length < 2 ? 'sheet' : null);
        }
      : undefined;

  const onSheetPick =
    mode === 'points' && picking === 'sheet'
      ? (u: number, v: number) => {
          setPairs([...pairs, { u, v, x: null, y: null }]);
          setPicking('map');
        }
      : undefined;

  const onSave = async () => {
    if (body === null) {
      return;
    }
    try {
      await save.mutateAsync({ id: raster.id, body });
      message.success(t('geodata.placement.saved'));
      onClose();
    } catch (error) {
      message.error(t(placementErrorKey(codeOf(error))));
    }
  };

  const onRemove = async () => {
    try {
      await clear.mutateAsync(raster.id);
      message.success(t('geodata.placement.removed'));
      onClose();
    } catch (error) {
      message.error(t(placementErrorKey(codeOf(error))));
    }
  };

  const fit = (() => {
    if (!answer || previewState !== 'ready') {
      return null;
    }
    if (mode === 'corners' && transformation === 'affine' && answer.residuals.length > 0) {
      return answer.residuals
        .map((r) => {
          const id = CORNER_IDS[r.index];
          const label = id ? t(CORNER_LABEL_KEYS[id]) : String(r.index + 1);
          return t('geodata.placement.residual', { corner: label, value: formatMetres(r.metres) });
        })
        .join(' · ');
    }
    return answer.rmsMetres === null ? null : t('geodata.placement.fit', { value: formatMetres(answer.rmsMetres) });
  })();

  const unusable = mode === 'corners' && corners !== null && !isUsableQuadrilateral(corners);

  return (
    <Flex vertical gap={12}>
      <Flex wrap gap={12} align="center">
        <Segmented<Mode>
          value={mode}
          onChange={(next) => {
            setMode(next);
            setPicking(null);
          }}
          options={[
            { value: 'corners', label: t('geodata.placement.modes.corners') },
            { value: 'points', label: t('geodata.placement.modes.points') },
          ]}
          data-testid="placement-mode"
        />
        {mode === 'corners' && (
          <Segmented<CornerTransformation>
            value={transformation}
            onChange={setTransformation}
            options={[
              { value: 'homography', label: t('geodata.placement.transformations.homography') },
              { value: 'affine', label: t('geodata.placement.transformations.affine') },
            ]}
            data-testid="placement-transformation"
          />
        )}
        <Flex align="center" gap={8} style={{ minWidth: 220 }}>
          <Typography.Text>{t('geodata.placement.opacity')}</Typography.Text>
          <Slider
            min={0.2}
            max={1}
            step={0.05}
            value={opacity}
            onChange={setOpacity}
            style={{ flex: 1 }}
            aria-label={t('geodata.placement.opacity')}
          />
        </Flex>
      </Flex>
      <Typography.Text type="secondary">
        {mode === 'corners'
          ? t(
              transformation === 'homography'
                ? 'geodata.placement.transformationHelp.homography'
                : 'geodata.placement.transformationHelp.affine',
            )
          : t('geodata.placement.pointsHelp')}
      </Typography.Text>

      <Flex gap={12} wrap>
        {mode === 'points' && (
          <Flex vertical gap={8} style={{ flex: '1 1 360px', minWidth: 0 }}>
            {raster.previewUrl ? (
              <SheetPicker
                imageUrl={raster.previewUrl}
                alt={raster.name}
                points={pairs}
                onPick={onSheetPick}
                active={active}
              />
            ) : (
              <Typography.Text type="secondary">{t('geodata.placement.noSheetPicture')}</Typography.Text>
            )}
          </Flex>
        )}
        <div style={{ flex: '2 1 480px', minWidth: 0 }}>
          <PlacementMap
            corners={corners}
            onCornersChange={setCorners}
            onViewReady={onViewReady}
            initialLonLatExtent={null}
            preview={image}
            opacity={opacity}
            mode={mode}
            points={pairs}
            onMapClick={onMapClick}
            active={active}
          />
        </div>
      </Flex>

      {mode === 'corners' && corners !== null && (
        <Collapse
          size="small"
          items={[
            {
              key: 'numbers',
              label: t('geodata.placement.typeCorners'),
              children: <CornerTable corners={corners} onChange={setCorners} />,
            },
          ]}
          data-testid="placement-corner-panel"
        />
      )}

      {mode === 'points' && (
        <Flex vertical gap={8}>
          <Flex gap={8} align="center" wrap>
            <Button
              icon={<AimOutlined />}
              disabled={picking !== null}
              onClick={() => setPicking('sheet')}
              data-testid="placement-add-point"
            >
              {t('geodata.placement.addPoint')}
            </Button>
            {picking !== null && (
              <Typography.Text data-testid="placement-picking">
                {picking === 'sheet' ? t('geodata.placement.pickOnSheet') : t('geodata.placement.pickOnMap')}
              </Typography.Text>
            )}
          </Flex>
          <ControlPointTable
            points={pairs}
            onChange={(index, next) => setPairs((now) => now.map((p, i) => (i === index ? next : p)))}
            onRemove={(index) => {
              setPairs((now) => now.filter((_, i) => i !== index));
              setPicking(null);
            }}
          />
        </Flex>
      )}

      {unusable && <Alert type="warning" showIcon title={t('geodata.placement.cornersCross')} />}
      {previewState === 'error' && previewError !== null && (
        <Alert type="error" showIcon title={previewError} data-testid="placement-preview-error" />
      )}

      <Flex justify="space-between" align="center" wrap gap={8}>
        <Typography.Text data-testid="placement-preview-state" data-state={previewState}>
          {previewState === 'pending' ? t('geodata.placement.previewing') : (fit ?? '')}
        </Typography.Text>
        <Flex gap={8}>
          {raster.placementSource === 'manual' && (
            <Popconfirm
              title={t('geodata.placement.removeConfirm')}
              onConfirm={() => void onRemove()}
              okButtonProps={{ danger: true }}
            >
              <Button danger loading={clear.isPending} data-testid="placement-remove">
                {t('geodata.placement.remove')}
              </Button>
            </Popconfirm>
          )}
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button
            type="primary"
            disabled={body === null}
            loading={save.isPending}
            onClick={() => void onSave()}
            data-testid="placement-save"
          >
            {t('common.save')}
          </Button>
        </Flex>
      </Flex>
    </Flex>
  );
}
