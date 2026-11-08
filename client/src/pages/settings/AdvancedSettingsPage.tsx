// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Flex, InputNumber, Radio, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { useMapConfig } from '../../api/hooks.ts';
import {
  CENTERLINE_MIN_PATHS,
  CENTERLINE_PATHS_STEP,
  CENTERLINE_ZOOM_RANGE,
} from '../../map/centerlineLimits.ts';
import {
  MESHES_IN_VIEW_MIN_BYTES,
  MESHES_IN_VIEW_ZOOM_RANGE,
} from '../../scene3d/surveyMeshesInView3d.ts';
import { SCANS_IN_VIEW_MIN_BYTES, SCANS_IN_VIEW_ZOOM_RANGE } from '../../scene3d/scansInView3d.ts';
import { coarsePointer } from '../../map/pointer.ts';
import {
  defaultPointDetailLevel,
  POINT_DETAIL_LEVELS,
  type PointDetailLevel,
} from '../../scene3d/pointCloudDetail.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

/** The word for each level, as a literal key so the key-parity checks can see every one. */
const POINT_DETAIL_LABEL_KEYS: Record<PointDetailLevel, string> = {
  low: 'settings.advanced.pointDetailLow',
  normal: 'settings.advanced.pointDetailNormal',
  high: 'settings.advanced.pointDetailHigh',
};

const MEGABYTE = 1024 * 1024;

/** A byte count as the megabytes a person reads and types: whole ones, and never "0". */
function megabytes(bytes: number): number {
  return Math.max(1, Math.round(bytes / MEGABYTE));
}

/**
 * Settings that tune how much the application asks of the machine in front of the person.
 *
 * Two cards: the three limits of the 3D view's "walls of every cave in view", and the two that
 * bound the survey lines the flat map draws. They are the installation's by default and a
 * person's own from here — for this browser only, because the right budget is a fact about a
 * graphics card, and the same person has a different one in their pocket. Nothing is saved to
 * the account, and nothing here is validated against the server: every field stops at the
 * ceiling the installation publishes, and a stored value is held to that ceiling again where it
 * is used — by the scene for the walls, by the server for the survey lines — since the ceiling
 * can be lowered afterwards.
 *
 * An empty field is not zero. It means "whatever this installation says", which is why the
 * installation's number is shown in its place rather than written into it: somebody who never
 * touches this page keeps following the operator when the operator changes their mind.
 *
 * Each card hands back its own numbers and no other's. The survey-line budgets can also be set
 * from the map's layer list, so one button for the page would let somebody undoing an experiment
 * with the walls discard, without looking at it, a number they chose somewhere else. Each card
 * is a named group for the same reason: two buttons that say the same thing are told apart by
 * what they belong to.
 */
export default function AdvancedSettingsPage() {
  const { t } = useTranslation();
  const { data: mapConfig } = useMapConfig();
  const minZoom = useUiPrefsStore((s) => s.meshesInViewMinZoom);
  const maxCaves = useUiPrefsStore((s) => s.meshesInViewMaxCaves);
  const maxBytes = useUiPrefsStore((s) => s.meshesInViewMaxBytes);
  const setLimits = useUiPrefsStore((s) => s.setMeshesInViewLimits);
  const askAbove = useUiPrefsStore((s) => s.meshAskAboveBytes);
  const setAskAbove = useUiPrefsStore((s) => s.setMeshAskAboveBytes);
  const detailZoom = useUiPrefsStore((s) => s.centerlineDetailZoom);
  const maxPaths = useUiPrefsStore((s) => s.centerlineMaxPaths);
  const setCenterlineLimits = useUiPrefsStore((s) => s.setCenterlineLimits);
  const pointDetail = useUiPrefsStore((s) => s.pointDetail);
  const setPointDetail = useUiPrefsStore((s) => s.setPointDetail);
  const scansMinZoom = useUiPrefsStore((s) => s.scansInViewMinZoom);
  const scansMaxCaves = useUiPrefsStore((s) => s.scansInViewMaxCaves);
  const scansMaxBytes = useUiPrefsStore((s) => s.scansInViewMaxBytes);
  const setScansLimits = useUiPrefsStore((s) => s.setScansInViewLimits);
  const scansMegabytesCeiling = mapConfig
    ? Math.max(1, Math.floor(mapConfig.scansInViewMaxBytesLimit / MEGABYTE))
    : undefined;
  const scansUntouched = scansMinZoom === undefined && scansMaxCaves === undefined && scansMaxBytes === undefined;

  // Rounded down: a megabyte figure that rounded up would offer a number the ceiling refuses.
  const megabytesCeiling = mapConfig
    ? Math.max(1, Math.floor(mapConfig.meshesInViewMaxBytesLimit / MEGABYTE))
    : undefined;
  const untouched = minZoom === undefined && maxCaves === undefined && maxBytes === undefined;
  const centerlinesUntouched = detailZoom === undefined && maxPaths === undefined;

  return (
    <Flex vertical gap={16}>
      <Card
        size="small"
        role="group"
        aria-label={t('settings.advanced.wallsHeading')}
        title={t('settings.advanced.wallsHeading')}
      >
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">{t('settings.advanced.wallsIntro')}</Typography.Text>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.advanced.wallsMinZoom')}
            </Typography.Text>
            <InputNumber
              min={MESHES_IN_VIEW_ZOOM_RANGE.min}
              max={MESHES_IN_VIEW_ZOOM_RANGE.max}
              precision={0}
              aria-label={t('settings.advanced.wallsMinZoom')}
              value={minZoom ?? null}
              placeholder={String(mapConfig?.meshesInViewMinZoom ?? '')}
              onChange={(value) => setLimits({ minZoom: value ?? undefined, maxCaves, maxBytes })}
            />
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.advanced.wallsMaxCaves')}
            </Typography.Text>
            <InputNumber
              min={1}
              max={mapConfig?.meshesInViewMaxCavesLimit}
              precision={0}
              aria-label={t('settings.advanced.wallsMaxCaves')}
              value={maxCaves ?? null}
              placeholder={String(mapConfig?.meshesInViewMaxCaves ?? '')}
              onChange={(value) => setLimits({ minZoom, maxCaves: value ?? undefined, maxBytes })}
            />
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.advanced.wallsMaxMegabytes')}
            </Typography.Text>
            {/* Typed in megabytes and kept in bytes, which is the unit the installation's own
                budget and every mesh's stated size are in. */}
            <InputNumber
              min={MESHES_IN_VIEW_MIN_BYTES / MEGABYTE}
              max={megabytesCeiling}
              precision={0}
              aria-label={t('settings.advanced.wallsMaxMegabytes')}
              value={maxBytes === undefined ? null : megabytes(maxBytes)}
              placeholder={mapConfig ? String(megabytes(mapConfig.meshesInViewMaxBytes)) : ''}
              onChange={(value) =>
                setLimits({
                  minZoom,
                  maxCaves,
                  maxBytes: value === null ? undefined : value * MEGABYTE,
                })
              }
            />
          </Flex>

          <Typography.Text type="secondary" data-testid="advanced-walls-hint">
            {t('settings.advanced.emptyHint')}
            {mapConfig && megabytesCeiling !== undefined && (
              <>
                {' '}
                {t('settings.advanced.wallsCeilings', {
                  caves: mapConfig.meshesInViewMaxCavesLimit,
                  megabytes: megabytesCeiling,
                })}
              </>
            )}
          </Typography.Text>

          <Flex>
            <Button disabled={untouched} onClick={() => setLimits({})}>
              {t('settings.advanced.useDefaults')}
            </Button>
          </Flex>
        </Flex>
      </Card>

      <Card
        size="small"
        role="group"
        aria-label={t('settings.advanced.wallsSelectedHeading')}
        title={t('settings.advanced.wallsSelectedHeading')}
      >
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">
            {t('settings.advanced.wallsSelectedIntro')}
          </Typography.Text>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.advanced.wallsAskAboveMegabytes')}
            </Typography.Text>
            {/* Typed in whole megabytes and kept in bytes, the unit every mesh's stated size is
                in. 0 is a real answer here — ask before every download — not "no number". */}
            <InputNumber
              min={0}
              precision={0}
              aria-label={t('settings.advanced.wallsAskAboveMegabytes')}
              value={askAbove === undefined ? null : Math.round(askAbove / MEGABYTE)}
              placeholder={
                mapConfig ? String(Math.round(mapConfig.meshAskAboveBytes / MEGABYTE)) : ''
              }
              onChange={(value) => setAskAbove(value === null ? undefined : value * MEGABYTE)}
            />
          </Flex>

          <Typography.Text type="secondary">{t('settings.advanced.emptyHint')}</Typography.Text>

          <Flex>
            <Button disabled={askAbove === undefined} onClick={() => setAskAbove(undefined)}>
              {t('settings.advanced.useDefaults')}
            </Button>
          </Flex>
        </Flex>
      </Card>

      <Card
        size="small"
        role="group"
        aria-label={t('settings.advanced.centerlinesHeading')}
        title={t('settings.advanced.centerlinesHeading')}
      >
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">{t('settings.advanced.centerlinesIntro')}</Typography.Text>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.advanced.centerlinesDetailZoom')}
            </Typography.Text>
            <InputNumber
              min={CENTERLINE_ZOOM_RANGE.min}
              max={CENTERLINE_ZOOM_RANGE.max}
              precision={0}
              aria-label={t('settings.advanced.centerlinesDetailZoom')}
              value={detailZoom ?? null}
              placeholder={String(mapConfig?.centerlineDetailZoom ?? '')}
              onChange={(value) => setCenterlineLimits({ detailZoom: value ?? undefined, maxPaths })}
            />
          </Flex>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.advanced.centerlinesMaxPaths')}
            </Typography.Text>
            <InputNumber
              min={CENTERLINE_MIN_PATHS}
              max={mapConfig?.centerlineMaxPathsLimit}
              step={CENTERLINE_PATHS_STEP}
              precision={0}
              aria-label={t('settings.advanced.centerlinesMaxPaths')}
              value={maxPaths ?? null}
              placeholder={String(mapConfig?.centerlineMaxPaths ?? '')}
              onChange={(value) => setCenterlineLimits({ detailZoom, maxPaths: value ?? undefined })}
            />
          </Flex>

          <Typography.Text type="secondary" data-testid="advanced-centerlines-hint">
            {t('settings.advanced.emptyHint')}
            {mapConfig && (
              <>
                {' '}
                {t('settings.advanced.centerlinesCeiling', { paths: mapConfig.centerlineMaxPathsLimit })}
              </>
            )}
          </Typography.Text>

          <Flex>
            <Button disabled={centerlinesUntouched} onClick={() => setCenterlineLimits({})}>
              {t('settings.advanced.useDefaults')}
            </Button>
          </Flex>
        </Flex>
      </Card>

      <Card
        size="small"
        role="group"
        aria-label={t('settings.advanced.scansHeading')}
        title={t('settings.advanced.scansHeading')}
        data-testid="advanced-scans-card"
      >
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">{t('settings.advanced.scansIntro')}</Typography.Text>
          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.advanced.wallsMinZoom')}</Typography.Text>
            <InputNumber
              min={SCANS_IN_VIEW_ZOOM_RANGE.min}
              max={SCANS_IN_VIEW_ZOOM_RANGE.max}
              precision={0}
              aria-label={t('settings.advanced.scansMinZoom')}
              value={scansMinZoom ?? null}
              placeholder={String(mapConfig?.scansInViewMinZoom ?? '')}
              onChange={(value) =>
                setScansLimits({ minZoom: value ?? undefined, maxCaves: scansMaxCaves, maxBytes: scansMaxBytes })
              }
            />
          </Flex>
          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.advanced.wallsMaxCaves')}</Typography.Text>
            <InputNumber
              min={1}
              max={mapConfig?.scansInViewMaxCavesLimit}
              precision={0}
              aria-label={t('settings.advanced.scansMaxCaves')}
              value={scansMaxCaves ?? null}
              placeholder={String(mapConfig?.scansInViewMaxCaves ?? '')}
              onChange={(value) =>
                setScansLimits({ minZoom: scansMinZoom, maxCaves: value ?? undefined, maxBytes: scansMaxBytes })
              }
            />
          </Flex>
          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>{t('settings.advanced.wallsMaxMegabytes')}</Typography.Text>
            <InputNumber
              min={SCANS_IN_VIEW_MIN_BYTES / MEGABYTE}
              max={scansMegabytesCeiling}
              precision={0}
              aria-label={t('settings.advanced.scansMaxMegabytes')}
              value={scansMaxBytes === undefined ? null : megabytes(scansMaxBytes)}
              placeholder={mapConfig ? String(megabytes(mapConfig.scansInViewMaxBytes)) : ''}
              onChange={(value) =>
                setScansLimits({
                  minZoom: scansMinZoom,
                  maxCaves: scansMaxCaves,
                  maxBytes: value === null ? undefined : value * MEGABYTE,
                })
              }
            />
          </Flex>
          <Typography.Text type="secondary">
            {t('settings.advanced.emptyHint')}
            {mapConfig && scansMegabytesCeiling !== undefined && (
              <>
                {' '}
                {t('settings.advanced.wallsCeilings', {
                  caves: mapConfig.scansInViewMaxCavesLimit,
                  megabytes: scansMegabytesCeiling,
                })}
              </>
            )}
          </Typography.Text>
          <Flex>
            <Button disabled={scansUntouched} onClick={() => setScansLimits({})}>
              {t('settings.advanced.useDefaults')}
            </Button>
          </Flex>
        </Flex>
      </Card>

      <Card
        size="small"
        role="group"
        aria-label={t('settings.advanced.pointDetailHeading')}
        title={t('settings.advanced.pointDetailHeading')}
      >
        <Flex vertical gap={16}>
          <Typography.Text type="secondary">{t('settings.advanced.pointDetailIntro')}</Typography.Text>

          <Flex gap={12} align="center" wrap>
            <Typography.Text style={{ minWidth: 160 }}>
              {t('settings.advanced.pointDetailLevel')}
            </Typography.Text>
            {/* Three words rather than two numbers: each level is a pair of the engine's dials, and
                the pairs are chosen once, in code, so nobody has to know what a screen-space error
                is. No level selected means the device's own default, said in the hint below. */}
            <Radio.Group
              optionType="button"
              aria-label={t('settings.advanced.pointDetailLevel')}
              value={pointDetail ?? null}
              onChange={(event) => setPointDetail(event.target.value as PointDetailLevel)}
              options={POINT_DETAIL_LEVELS.map((level) => ({
                value: level,
                label: t(POINT_DETAIL_LABEL_KEYS[level]),
              }))}
              data-testid="advanced-point-detail"
            />
          </Flex>

          <Typography.Text type="secondary" data-testid="advanced-point-detail-hint">
            {t('settings.advanced.pointDetailDefault', {
              level: t(POINT_DETAIL_LABEL_KEYS[defaultPointDetailLevel(coarsePointer())]),
            })}
          </Typography.Text>

          <Flex>
            <Button disabled={pointDetail === undefined} onClick={() => setPointDetail(undefined)}>
              {t('settings.advanced.useDefaults')}
            </Button>
          </Flex>
        </Flex>
      </Card>
    </Flex>
  );
}
