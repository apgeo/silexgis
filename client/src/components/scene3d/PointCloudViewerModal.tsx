// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Flex, Modal, Radio, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import Button from '../Button.tsx';
import { coarsePointer } from '../../map/pointer.ts';
import { defaultColoring } from '../../scene3d/pointCloudColoring.ts';
import { pointCloudDetailInForce } from '../../scene3d/pointCloudDetail.ts';
import type {
  Scene3DLocalScan,
  Scene3DMeasurement,
  Scene3DPointCloudColoring,
  Scene3DPointCloudStatus,
} from '../../scene3d/scene3dEngine.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

interface PointCloudViewerModalProps {
  open: boolean;
  /** The signed address of the tileset's root, as the server handed it out. */
  tilesetUrl: string;
  name: string;
  /** Whether the tiles carry a recorded colour, and a return strength: which colourings are offered. */
  hasRgb: boolean;
  hasIntensity: boolean;
  onClose(): void;
}

const COLORING_LABEL_KEYS: Record<Scene3DPointCloudColoring, string> = {
  rgb: 'scene3d.pointCloud.colorByRgb',
  intensity: 'scene3d.pointCloud.colorByIntensity',
  height: 'scene3d.pointCloud.colorByHeight',
};

/**
 * A scan on its own, over the whole window: the local scene with no globe, the cloud in its own
 * metres, orbit by dragging, colour by what the file carries or by height.
 *
 * Built and destroyed with the modal rather than kept, because the scene holds a 3D drawing
 * context and a browser keeps only a handful of those alive — the map and the 3D view draw from
 * the same small budget. The engine module is loaded on demand for the same reason it is
 * everywhere else: it is the one heavy library of the application and a cave page that never
 * opens a scan must not pay for it.
 */
export default function PointCloudViewerModal({
  open,
  tilesetUrl,
  name,
  hasRgb,
  hasIntensity,
  onClose,
}: PointCloudViewerModalProps) {
  const { t, i18n } = useTranslation();
  const pointDetail = useUiPrefsStore((s) => s.pointDetail);
  // The surface is state rather than a ref: the modal mounts its content a tick after it opens,
  // so an effect keyed on `open` alone runs before there is an element to draw into. Keyed on
  // the element, the scene is built when the surface appears and destroyed when it goes.
  const [surface, setSurface] = useState<HTMLDivElement | null>(null);
  const scene = useRef<Scene3DLocalScan | null>(null);
  const [status, setStatus] = useState<Scene3DPointCloudStatus | undefined>(undefined);
  const [renderError, setRenderError] = useState<string | null>(null);
  const [coloring, setColoring] = useState<Scene3DPointCloudColoring>(() => defaultColoring(hasRgb, hasIntensity));
  const [measuring, setMeasuring] = useState(false);
  const [measurement, setMeasurement] = useState<Scene3DMeasurement | undefined>(undefined);

  useEffect(() => {
    if (!open || !surface) {
      return;
    }
    const element = surface;
    let cancelled = false;
    let built: Scene3DLocalScan | null = null;
    setStatus('loading');
    setRenderError(null);
    setMeasuring(false);
    setMeasurement(undefined);
    void (async () => {
      const { createLocalScan3D } = await import('../../scene3d/scene3dContext.ts');
      if (cancelled) {
        return;
      }
      try {
        built = createLocalScan3D(element);
      } catch {
        setStatus('failed');
        return;
      }
      scene.current = built;
      built.subscribeRenderError((message) => setRenderError(message));
      built.subscribeMeasurement((next) => setMeasurement(next));
      try {
        await built.load({
          url: tilesetUrl,
          detail: pointCloudDetailInForce(useUiPrefsStore.getState().pointDetail, coarsePointer()),
          style: { colorBy: defaultColoring(hasRgb, hasIntensity) },
        });
        if (!cancelled) {
          setStatus('loaded');
        }
      } catch {
        if (!cancelled) {
          setStatus('failed');
        }
      }
    })();
    return () => {
      cancelled = true;
      scene.current = null;
      built?.destroy();
    };
    // The address and the colouring are read once at build: a new signed address for the same
    // tiles is not a reason to rebuild a scene, and the colouring is applied through setStyle.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- rebuilt only when opened or the surface changes
  }, [open, surface]);

  // The detail setting changes while a scan is open — Settings in another tab — and the scene
  // takes the new pair without a reload.
  useEffect(() => {
    scene.current?.setDetail(pointCloudDetailInForce(pointDetail, coarsePointer()));
  }, [pointDetail]);

  const choose = (next: Scene3DPointCloudColoring) => {
    setColoring(next);
    scene.current?.setStyle({ colorBy: next });
  };

  const toggleMeasure = () => {
    const next = !measuring;
    setMeasuring(next);
    scene.current?.setMeasuring(next);
  };

  // What the measure has to say: where it stands until two points are taken, then the metres
  // between them — to the centimetre, which is finer than a scan's registration and as fine as
  // anybody reads a passage width.
  const measureReadout = !measuring
    ? null
    : measurement?.points === 2 && measurement.distanceM !== undefined
      ? t('scene3d.pointCloud.measureDistance', {
          metres: new Intl.NumberFormat(i18n.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(
            measurement.distanceM,
          ),
        })
      : measurement?.points === 1
        ? t('scene3d.pointCloud.measureFirstPoint')
        : t('scene3d.pointCloud.measureHint');

  const offered: Scene3DPointCloudColoring[] = [
    ...(hasRgb ? (['rgb'] as const) : []),
    ...(hasIntensity ? (['intensity'] as const) : []),
    'height',
  ];

  return (
    <Modal
      open={open}
      onCancel={onClose}
      footer={null}
      width="min(1200px, 95vw)"
      destroyOnHidden
      title={t('scene3d.pointCloud.viewerTitle', { name })}
    >
      {/* The id is on the content, not the dialog: the dialog's root is a box-less wrapper that
          a test cannot see, and the content is what a person sees. */}
      <Flex vertical gap={8} data-testid="point-cloud-viewer">
        <Flex gap={12} align="center" wrap>
          <Typography.Text>{t('scene3d.pointCloud.colorBy')}</Typography.Text>
          <Radio.Group
            optionType="button"
            size="small"
            value={coloring}
            onChange={(event) => choose(event.target.value as Scene3DPointCloudColoring)}
            options={offered.map((value) => ({ value, label: t(COLORING_LABEL_KEYS[value]) }))}
            data-testid="point-cloud-color-by"
          />
          <Button size="small" onClick={() => scene.current?.viewAll()} data-testid="point-cloud-view-all">
            {t('scene3d.pointCloud.viewAll')}
          </Button>
          <Button
            size="small"
            type={measuring ? 'primary' : 'default'}
            aria-pressed={measuring}
            disabled={status !== 'loaded'}
            onClick={toggleMeasure}
            data-testid="point-cloud-measure"
          >
            {t(measuring ? 'scene3d.pointCloud.measureOff' : 'scene3d.pointCloud.measure')}
          </Button>
          {measureReadout !== null && (
            <Typography.Text data-testid="point-cloud-measure-readout" aria-live="polite">
              {measureReadout}
            </Typography.Text>
          )}
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t('scene3d.pointCloud.localFrameHint')}
          </Typography.Text>
        </Flex>
        {status === 'failed' && (
          <Alert type="error" showIcon title={t('scene3d.pointCloud.failed')} data-testid="point-cloud-failed" />
        )}
        {renderError !== null && (
          <Alert type="error" showIcon title={t('scene3d.pointCloud.renderFailed', { message: renderError })} />
        )}
        <div style={{ position: 'relative', height: '70vh', background: '#101014' }}>
          <div
            ref={setSurface}
            data-testid="point-cloud-surface"
            data-status={status ?? ''}
            style={{ position: 'absolute', inset: 0 }}
          />
          {status === 'loading' && (
            <Flex
              align="center"
              justify="center"
              gap={8}
              style={{ position: 'absolute', inset: 0, pointerEvents: 'none', color: '#fff' }}
            >
              <Spin />
              <Typography.Text style={{ color: '#fff' }}>{t('scene3d.pointCloud.loading')}</Typography.Text>
            </Flex>
          )}
        </div>
      </Flex>
    </Modal>
  );
}
