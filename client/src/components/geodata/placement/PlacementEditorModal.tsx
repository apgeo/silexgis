// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { AimOutlined, DownloadOutlined, EnvironmentOutlined, ExpandOutlined, UploadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Collapse,
  Flex,
  Modal,
  Popconfirm,
  Result,
  Segmented,
  Select,
  Slider,
  Spin,
  Typography,
  Upload,
} from 'antd';
import { useNavigate } from 'react-router-dom';
import { toLonLat as projectedToLonLat } from 'ol/proj';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../../api/client.ts';
import { saveBlob } from '../../../api/download.ts';
import {
  fetchRasterAnnotation,
  useClearRasterPlacement,
  useImportRasterAnnotation,
  useMapLayers,
  usePlacementSystems,
  usePreviewRasterPlacement,
  useRasterMap,
  useSaveRasterPlacement,
  useSheetStationPoints,
  type RasterMapInfo,
  type RasterMapPlacementPreview,
} from '../../../api/hooks.ts';
import { placementErrorKey } from '../../../geodata/placement/errors.ts';
import {
  CORNER_IDS,
  TRANSFORMATIONS,
  annotationFileName,
  completePairs,
  cornersOfPlacement,
  cornersToPlacement,
  cornerTransformationOf,
  formatMetres,
  initialCorners,
  isUsableQuadrilateral,
  isWktCrs,
  maskOf,
  minimumPoints,
  orderSystems,
  outlierRows,
  pairsOfPlacement,
  pairsToPlacement,
  previewImageOptions,
  residualsByPair,
  sheetAspect,
  stationPairs,
  suggestedTransformation,
  type CornerTransformation,
  type Corners,
  type PlacementBody,
  type PlacementTransformation,
  type PointPair,
  type TypedSystem,
} from '../../../geodata/placement/placement.ts';
import { chooseBackground, workspaceBackgroundId } from '../../../map/editorBackground.ts';
import { fitGeoJsonGeometry } from '../../../map/mapContext.ts';
import { useWorkspaceStore } from '../../../stores/workspaceStore.ts';
import Button from '../../Button.tsx';
import CaveLinkWarning from '../CaveLinkWarning.tsx';
import ControlPointTable from './ControlPointTable.tsx';
import { CORNER_LABEL_KEYS } from './cornerLabels.ts';
import CornerTable from './CornerTable.tsx';
import * as crsMath from './crs.ts';
import PlacementMap, { type PlacementMarker } from './PlacementMap.tsx';
import SheetPicker, { type Fraction } from './SheetPicker.tsx';
import { TRANSFORMATION_KEYS } from './transformationLabels.ts';

type Mode = 'corners' | 'points' | 'controls';

/** How long after the last change the preview is asked for, so a burst of edits asks once. */
const PREVIEW_DELAY_MS = 150;

interface Props {
  /** The sheet being placed; null while the dialog is closed. */
  raster: RasterMapInfo | null;
  onClose: () => void;
}

/**
 * Lays a drawn map sheet on the map: by its four corners, by two points matched between the sheet
 * and the ground, or by as many control points as the sheet needs, with a choice of how the sheet
 * may bend and an outline that leaves its margins out. Every change asks the server for a preview
 * of the sheet where the points put it, drawn over the map; Save keeps the placement, and the
 * server then publishes the sheet in the background.
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
      width="min(1400px, 96vw)"
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

/**
 * Where the editor opens: the document's own placement is reviewed point by point, a placement of
 * the four corners in the editor's own shape reopens on its corners, two points on the two-point
 * mode, and anything else on the control points.
 */
function initialMode(raster: RasterMapInfo): Mode {
  const placement = raster.placement ?? null;
  if (!placement) {
    return 'corners';
  }
  if (raster.placementSource === 'embedded') {
    return 'controls';
  }
  if (cornersOfPlacement(placement) !== null && !placement.mask) {
    return 'corners';
  }
  if (placement.transformation === 'similarity' && placement.gcps.length === 2 && !placement.mask) {
    return 'points';
  }
  return 'controls';
}

function readText(file: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ''));
    reader.onerror = () => reject(reader.error ?? new Error('unreadable'));
    reader.readAsText(file);
  });
}

function PlacementEditor({ raster, onClose, active }: { raster: RasterMapInfo; onClose: () => void; active: boolean }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const preview = usePreviewRasterPlacement();
  const save = useSaveRasterPlacement();
  const clear = useClearRasterPlacement();
  const importAnnotation = useImportRasterAnnotation();
  const navigate = useNavigate();
  const setRasterVisible = useWorkspaceStore((s) => s.setRasterVisible);
  const { data: catalogue } = useMapLayers();
  // Read once: the editor draws over whatever the reader had on the workspace map when it opened.
  const [activeBackgroundId] = useState(() => workspaceBackgroundId());
  const background = useMemo(() => chooseBackground(catalogue, activeBackgroundId), [catalogue, activeBackgroundId]);
  const { data: listedSystems } = usePlacementSystems();
  // Set once a write has been accepted: the dialog then watches the map until it is published.
  const [publishing, setPublishing] = useState(false);
  const { data: watched } = useRasterMap(raster.id, publishing);

  const existing = raster.placement ?? null;
  const embedded = raster.placementSource === 'embedded';
  const [mode, setMode] = useState<Mode>(() => initialMode(raster));
  const opensOnPoints = mode !== 'corners';
  const [corners, setCorners] = useState<Corners | null>(() => cornersOfPlacement(existing));
  const [transformation, setTransformation] = useState<CornerTransformation>(() => cornerTransformationOf(existing));
  const [pairs, setPairs] = useState<PointPair[]>(() => (opensOnPoints ? pairsOfPlacement(existing) : []));
  const [crs, setCrs] = useState<string>(() => (opensOnPoints && existing ? existing.crs : 'EPSG:4326'));
  const [crsReady, setCrsReady] = useState(() => crsMath.inDegrees(crs));
  const [controlTransformation, setControlTransformation] = useState<PlacementTransformation>(() =>
    opensOnPoints && existing && (TRANSFORMATIONS as readonly string[]).includes(existing.transformation)
      ? (existing.transformation as PlacementTransformation)
      : 'affine',
  );
  const [mask, setMask] = useState<Fraction[] | null>(() => (opensOnPoints ? maskOf(existing) : null));
  const [drawingMask, setDrawingMask] = useState(false);
  const [picking, setPicking] = useState<'sheet' | 'map' | null>(null);
  const [fitRequest, setFitRequest] = useState(0);
  const [opacity, setOpacity] = useState(0.7);
  const [answer, setAnswer] = useState<RasterMapPlacementPreview | null>(null);
  const [previewState, setPreviewState] = useState<'idle' | 'pending' | 'ready' | 'error'>('idle');
  const [previewError, setPreviewError] = useState<string | null>(null);

  const documentCrs = isWktCrs(crs);
  const pointMode = mode !== 'corners';
  const { data: pinned } = useSheetStationPoints(raster.id, mode === 'controls' && raster.sourceFileId != null);

  // The systems the server offers, less any this installation cannot define for conversion.
  const ordered = useMemo(() => orderSystems(listedSystems), [listedSystems]);
  const [usable, setUsable] = useState<TypedSystem[]>(() => ordered.filter((s) => crsMath.inDegrees(s.crs)));
  useEffect(() => {
    let current = true;
    void Promise.all(ordered.map((system) => crsMath.prepareCrs(system.crs))).then((answers) => {
      if (current) {
        setUsable(ordered.filter((_, index) => answers[index]));
      }
    });
    return () => {
      current = false;
    };
  }, [ordered]);
  const degrees = documentCrs ? false : (usable.find((s) => s.crs === crs)?.geographic ?? crsMath.inDegrees(crs));

  // A write was accepted: once the map is published the dialog offers to show it; a publishing
  // that failed sends the sheet back to "needs placement" with the reason, and editing resumes.
  useEffect(() => {
    if (!publishing || !watched) {
      return;
    }
    if (watched.status === 'unplaced' || watched.status === 'failed') {
      setPublishing(false);
      message.error(watched.processingError || t('geodata.placement.publishFailed'));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reacts to the watched answer only
  }, [publishing, watched]);
  const pointsTransformation: PlacementTransformation = mode === 'points' ? 'similarity' : controlTransformation;
  const needed = minimumPoints(pointsTransformation);
  const complete = completePairs(pairs).length;
  // The frame the document itself draws round its map, offered while its own placement is shown.
  const frame = embedded ? maskOf(existing) : null;

  // A grid's definition comes from the installation's registry; until it has, nothing in it can
  // be drawn on the map or converted from a click.
  useEffect(() => {
    let current = true;
    void crsMath.prepareCrs(crs).then((ok) => {
      if (current) {
        setCrsReady(ok);
      }
    });
    return () => {
      current = false;
    };
  }, [crs]);

  const body = useMemo<PlacementBody | null>(() => {
    if (mode === 'corners') {
      return corners && isUsableQuadrilateral(corners) ? cornersToPlacement(corners, transformation) : null;
    }
    return pairsToPlacement(pairs, pointsTransformation, { crs, mask });
  }, [mode, corners, transformation, pairs, pointsTransformation, crs, mask]);
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

  const residuals = useMemo(
    () => (pointMode && answer && previewState === 'ready' ? residualsByPair(pairs, answer.residuals) : undefined),
    [pointMode, answer, previewState, pairs],
  );
  const outliers = useMemo(
    () => (residuals && answer ? outlierRows(residuals, answer.rmsMetres) : new Set<number>()),
    [residuals, answer],
  );

  const markers = useMemo<PlacementMarker[]>(
    () =>
      pairs.map((pair, index) => ({
        lonLat: pair.x !== null && pair.y !== null && crsReady ? crsMath.toLonLat(pair.x, pair.y, crs) : null,
        label: pair.label?.trim() || String(index + 1),
        outlier: outliers.has(index),
      })),
    [pairs, crs, crsReady, outliers],
  );

  const onViewReady = (extent3857: [number, number, number, number]) => {
    if (corners !== null) {
      return;
    }
    const planar = initialCorners(extent3857, sheetAspect(raster.renderWidth, raster.renderHeight));
    const lonLat = Object.fromEntries(
      CORNER_IDS.map((id) => [id, projectedToLonLat(planar[id]) as [number, number]]),
    ) as Corners;
    setCorners(lonLat);
  };

  /** Where picking goes once a pair has both halves: the two-point mode stops at two. */
  const afterComplete = (count: number) => setPicking(mode === 'controls' || count < 2 ? 'sheet' : null);

  const onMapClick =
    pointMode && picking === 'map' && !documentCrs
      ? ([lon, lat]: [number, number]) => {
          const last = pairs.length - 1;
          const at = crsReady ? crsMath.fromLonLat(lon, lat, crs) : null;
          if (last < 0 || pairs[last].x !== null || at === null) {
            return;
          }
          setPairs(pairs.map((pair, index) => (index === last ? { ...pair, x: at[0], y: at[1] } : pair)));
          afterComplete(pairs.length);
        }
      : undefined;

  const onSheetPick =
    pointMode && picking === 'sheet' && !documentCrs
      ? (u: number, v: number) => {
          setPairs([...pairs, { u, v, x: null, y: null }]);
          setPicking('map');
        }
      : undefined;

  const onRowChange = (index: number, next: PointPair) => {
    const updated = pairs.map((pair, i) => (i === index ? next : pair));
    setPairs(updated);
    // A pending point's ground half may be typed instead of clicked.
    if (picking === 'map' && index === pairs.length - 1 && next.x !== null && next.y !== null) {
      afterComplete(updated.length);
    }
  };

  const onMarkerMove =
    pointMode && !documentCrs
      ? (index: number, [lon, lat]: [number, number]) => {
          const at = crsReady ? crsMath.fromLonLat(lon, lat, crs) : null;
          if (at) {
            setPairs((now) => now.map((pair, i) => (i === index ? { ...pair, x: at[0], y: at[1] } : pair)));
          }
        }
      : undefined;

  const onSheetMove =
    pointMode && !documentCrs
      ? (index: number, u: number, v: number) =>
          setPairs((now) => now.map((pair, i) => (i === index ? { ...pair, u, v } : pair)))
      : undefined;

  const onCrsChange = async (next: string) => {
    if (next === crs) {
      return;
    }
    if (!(await crsMath.prepareCrs(next))) {
      message.error(t('geodata.placement.crsUnavailable'));
      return;
    }
    setPairs(crsMath.convertPairs(pairs, crs, next));
    setCrs(next);
  };

  const startFresh = () => {
    setPairs([]);
    setCrs('EPSG:4326');
    setPicking(null);
  };

  const onSave = async () => {
    if (body === null) {
      return;
    }
    try {
      await save.mutateAsync({ id: raster.id, body });
      message.success(t('geodata.placement.saved'));
      setPublishing(true);
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

  const onExport = async () => {
    try {
      const annotation = await fetchRasterAnnotation(raster.id);
      saveBlob(
        new Blob([JSON.stringify(annotation, null, 2)], { type: 'application/json' }),
        annotationFileName(raster.name),
      );
    } catch (error) {
      message.error(t(placementErrorKey(codeOf(error))));
    }
  };

  const onImport = async (file: Blob) => {
    let annotation: unknown;
    try {
      annotation = JSON.parse(await readText(file));
    } catch {
      message.error(t('geodata.placement.importNotJson'));
      return;
    }
    try {
      await importAnnotation.mutateAsync({ id: raster.id, annotation });
      message.success(t('geodata.placement.imported'));
      setPublishing(true);
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
    if (answer.rmsMetres === null) {
      return pointMode ? t('geodata.placement.fitNotMeasured') : null;
    }
    return t('geodata.placement.fit', { value: formatMetres(answer.rmsMetres) });
  })();

  const unusable = mode === 'corners' && corners !== null && !isUsableQuadrilateral(corners);
  const tooFew = pointMode && complete < needed;
  const transformationName = t(TRANSFORMATION_KEYS[pointsTransformation].name);

  const helpText =
    mode === 'corners'
      ? t(
          transformation === 'homography'
            ? 'geodata.placement.transformationHelp.homography'
            : 'geodata.placement.transformationHelp.affine',
        )
      : mode === 'points'
        ? t('geodata.placement.pointsHelp')
        : t('geodata.placement.controlsHelp');

  const crsOptions = [
    ...usable.map((system) => ({
      value: system.crs,
      label: system.working ? `${system.name} ${t('geodata.placement.workingSystem')}` : system.name,
    })),
    ...(documentCrs ? [{ value: crs, label: t('geodata.placement.crs.document'), disabled: true }] : []),
  ];

  const addPinnedStations = () => {
    const added = stationPairs(pinned ?? [], pairs, (lon, lat) => crsMath.fromLonLat(lon, lat, crs));
    if (added.length === 0) {
      return;
    }
    const next = [...pairs, ...added];
    setPairs(next);
    setPicking(null);
    const suggestion = suggestedTransformation(completePairs(next).length);
    if (suggestion) {
      setControlTransformation(suggestion);
    }
  };

  const showOnMap = (published: RasterMapInfo) => {
    setRasterVisible(published.id, true);
    if (published.bbox) {
      fitGeoJsonGeometry(published.bbox);
    }
    onClose();
    navigate('/map');
  };

  if (publishing) {
    const done = watched?.status === 'ready' ? watched : null;
    return done ? (
      <Result
        status="success"
        title={t('geodata.placement.published')}
        data-testid="placement-published"
        extra={[
          <Button
            key="show"
            type="primary"
            icon={<EnvironmentOutlined />}
            onClick={() => showOnMap(done)}
            data-testid="placement-show-on-map"
          >
            {t('geodata.showOnMap')}
          </Button>,
          <Button key="close" onClick={onClose} data-testid="placement-close">
            {t('common.close')}
          </Button>,
        ]}
      />
    ) : (
      <Flex vertical align="center" gap={12} style={{ padding: 48 }} data-testid="placement-publishing">
        <Spin size="large" />
        <Typography.Text>{t('geodata.placement.publishing')}</Typography.Text>
        <Button onClick={onClose}>{t('common.close')}</Button>
      </Flex>
    );
  }

  return (
    <Flex vertical gap={12}>
      <Flex wrap gap={12} align="center">
        <Segmented<Mode>
          value={mode}
          onChange={(next) => {
            setMode(next);
            setPicking(null);
            setDrawingMask(false);
          }}
          options={[
            { value: 'corners', label: t('geodata.placement.modes.corners') },
            { value: 'points', label: t('geodata.placement.modes.points') },
            { value: 'controls', label: t('geodata.placement.modes.controls') },
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
        {mode === 'controls' && (
          <Flex align="center" gap={8}>
            <Typography.Text>{t('geodata.placement.transformation')}</Typography.Text>
            <Select<PlacementTransformation>
              value={controlTransformation}
              onChange={setControlTransformation}
              style={{ width: 300 }}
              options={TRANSFORMATIONS.map((value) => ({
                value,
                label: `${t(TRANSFORMATION_KEYS[value].name)} — ${t('geodata.placement.minimumPoints', {
                  min: minimumPoints(value),
                })}`,
              }))}
              aria-label={t('geodata.placement.transformation')}
              data-testid="placement-control-transformation"
            />
          </Flex>
        )}
        {pointMode && (
          <Flex align="center" gap={8}>
            <Typography.Text>{t('geodata.placement.crsLabel')}</Typography.Text>
            <Select<string>
              value={crs}
              onChange={(next) => void onCrsChange(next)}
              style={{ width: 260 }}
              options={crsOptions}
              disabled={documentCrs}
              aria-label={t('geodata.placement.crsLabel')}
              data-testid="placement-crs"
            />
          </Flex>
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
        {helpText}
        {mode === 'controls' && ` ${t(TRANSFORMATION_KEYS[controlTransformation].explain)}`}
      </Typography.Text>
      {raster.sourceFileId != null && raster.caveFeatureId != null && <CaveLinkWarning />}
      {pointMode && embedded && !documentCrs && (
        <Alert type="info" showIcon title={t('geodata.placement.embeddedNote')} />
      )}
      {pointMode && documentCrs && (
        <Alert
          type="info"
          showIcon
          title={t('geodata.placement.documentCrsNote')}
          action={
            <Button size="small" onClick={startFresh} data-testid="placement-start-fresh">
              {t('geodata.placement.startFresh')}
            </Button>
          }
        />
      )}

      <Flex gap={12} wrap>
        {pointMode && (
          <Flex vertical gap={8} style={{ flex: '1 1 360px', minWidth: 0 }}>
            {raster.previewUrl ? (
              <SheetPicker
                imageUrl={raster.previewUrl}
                alt={raster.name}
                size={{ width: raster.renderWidth ?? 1000, height: raster.renderHeight ?? 1000 }}
                points={pairs}
                outliers={outliers}
                onPick={onSheetPick}
                onMovePoint={onSheetMove}
                mask={mask}
                drawingMask={drawingMask}
                onMaskChange={(next) => {
                  setMask(next && next.length >= 3 ? next : null);
                  setDrawingMask(false);
                }}
                active={active}
              />
            ) : (
              <Typography.Text type="secondary">{t('geodata.placement.noSheetPicture')}</Typography.Text>
            )}
            <Flex gap={8} wrap align="center">
              <Typography.Text strong>{t('geodata.placement.outline')}</Typography.Text>
              <Button size="small" onClick={() => setDrawingMask(!drawingMask)} data-testid="placement-draw-outline">
                {drawingMask ? t('geodata.placement.stopDrawing') : t('geodata.placement.drawOutline')}
              </Button>
              <Button
                size="small"
                disabled={mask === null}
                onClick={() => setMask(null)}
                data-testid="placement-clear-outline"
              >
                {t('geodata.placement.clearOutline')}
              </Button>
              {frame && (
                <Button size="small" onClick={() => setMask(frame)} data-testid="placement-use-frame">
                  {t('geodata.placement.useFrame')}
                </Button>
              )}
            </Flex>
            {drawingMask && <Typography.Text type="secondary">{t('geodata.placement.outlineHelp')}</Typography.Text>}
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
            markers={markers}
            onMarkerMove={onMarkerMove}
            fitRequest={fitRequest}
            onMapClick={onMapClick}
            active={active}
            background={background}
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

      {pointMode && (
        <Flex vertical gap={8}>
          <Flex gap={8} align="center" wrap>
            {mode === 'controls' && picking !== null ? (
              <Button onClick={() => setPicking(null)} data-testid="placement-stop-adding">
                {t('geodata.placement.stopAdding')}
              </Button>
            ) : (
              <Button
                icon={<AimOutlined />}
                disabled={picking !== null || documentCrs}
                onClick={() => setPicking('sheet')}
                data-testid="placement-add-point"
              >
                {mode === 'controls' ? t('geodata.placement.addPoints') : t('geodata.placement.addPoint')}
              </Button>
            )}
            {mode === 'controls' && (pinned?.length ?? 0) > 0 && !documentCrs && (
              <Button onClick={addPinnedStations} data-testid="placement-use-stations">
                {t('geodata.placement.useStations', { n: pinned?.length ?? 0 })}
              </Button>
            )}
            <Button
              icon={<ExpandOutlined />}
              disabled={!markers.some((m) => m.lonLat !== null)}
              onClick={() => setFitRequest((n) => n + 1)}
              data-testid="placement-fit-points"
            >
              {t('geodata.placement.fitToPoints')}
            </Button>
            {picking !== null && (
              <Typography.Text data-testid="placement-picking">
                {picking === 'sheet' ? t('geodata.placement.pickOnSheet') : t('geodata.placement.pickOnMap')}
              </Typography.Text>
            )}
          </Flex>
          <ControlPointTable
            points={pairs}
            degrees={degrees}
            readOnly={documentCrs}
            showLabels={mode === 'controls'}
            residuals={residuals}
            outliers={outliers}
            onChange={onRowChange}
            onRemove={(index) => {
              setPairs((now) => now.filter((_, i) => i !== index));
              setPicking(null);
            }}
          />
        </Flex>
      )}

      {unusable && <Alert type="warning" showIcon title={t('geodata.placement.cornersCross')} />}
      {tooFew && (
        <Alert
          type="info"
          showIcon
          title={t('geodata.placement.needsPoints', { name: transformationName, min: needed, have: complete })}
          data-testid="placement-too-few"
        />
      )}
      {previewState === 'error' && previewError !== null && (
        <Alert type="error" showIcon title={previewError} data-testid="placement-preview-error" />
      )}

      <Flex justify="space-between" align="center" wrap gap={8}>
        <Typography.Text data-testid="placement-preview-state" data-state={previewState}>
          {previewState === 'pending' ? t('geodata.placement.previewing') : (fit ?? '')}
        </Typography.Text>
        <Flex gap={8} wrap>
          <Button
            icon={<DownloadOutlined />}
            disabled={existing === null}
            onClick={() => void onExport()}
            data-testid="placement-export"
          >
            {t('geodata.placement.export')}
          </Button>
          <Upload
            accept=".json,application/json"
            showUploadList={false}
            beforeUpload={(file) => {
              void onImport(file);
              return false;
            }}
          >
            <Button icon={<UploadOutlined />} loading={importAnnotation.isPending} data-testid="placement-import">
              {t('geodata.placement.import')}
            </Button>
          </Upload>
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
