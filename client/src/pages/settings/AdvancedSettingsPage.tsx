// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, Card, Flex, InputNumber, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMapConfig } from '../../api/hooks.ts';
import {
  MESHES_IN_VIEW_MIN_BYTES,
  MESHES_IN_VIEW_ZOOM_RANGE,
} from '../../scene3d/surveyMeshesInView3d.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';

const MEGABYTE = 1024 * 1024;

/** The zooms full survey detail may be asked for from: the range a map of this kind is ever drawn at. */
const CENTERLINE_ZOOM_RANGE = { min: 1, max: 22 } as const;

/**
 * The smallest line budget a person may set. Lower withholds all but the smallest survey, which
 * reads on the map as the overlay being broken rather than as a choice.
 */
const CENTERLINE_MIN_PATHS = 100;

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
  const detailZoom = useUiPrefsStore((s) => s.centerlineDetailZoom);
  const maxPaths = useUiPrefsStore((s) => s.centerlineMaxPaths);
  const setCenterlineLimits = useUiPrefsStore((s) => s.setCenterlineLimits);

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
              step={1000}
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
    </Flex>
  );
}
