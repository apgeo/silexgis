// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TerrainBuild } from '../../../api/hooks.ts';
import {
  TERRAIN_BAKE_TIMEOUT_SETTING,
  TERRAIN_WORKER_APPLY_COMMAND,
  TERRAIN_WORKER_MEMORY_SETTING,
  hoursOf,
} from './terrainBuild.ts';

/**
 * The two limits a bake runs under, and what each means when it is reached.
 *
 * Both are set by whoever deploys the installation, not on this page, and neither announces itself:
 * the tile maker short of memory does not fail, it quietly makes coarser terrain, and a bake that
 * is merely large looks from outside exactly like one that has stopped. So what each limit does is
 * said before a build is started, and what to change is said — with the line to paste — on the
 * build that met it.
 */

/**
 * The note beside the button that starts a build: memory decides how fine the result can be, and
 * running short shows up as coarser terrain rather than as an error.
 */
export function TerrainMemoryNote() {
  const { t } = useTranslation();

  return (
    <Typography.Paragraph type="secondary" data-testid="terrain-memory-note">
      {t('terrain.bakeLimits.memoryNote')}
    </Typography.Paragraph>
  );
}

/** What a build that ran short of memory is told: what coarser means, and how to give it more. */
export function TerrainBakeDegraded() {
  const { t } = useTranslation();

  return (
    <Alert
      type="warning"
      showIcon
      data-testid="terrain-build-degraded"
      title={t('terrain.bakeLimits.degradedTitle')}
      description={
        <Flex vertical gap={8}>
          <Typography.Text>{t('terrain.bakeLimits.degradedBody')}</Typography.Text>
          <div>
            <Typography.Text>{t('terrain.bakeLimits.degradedSetting')}</Typography.Text>
            <Typography.Paragraph code copyable style={{ margin: '4px 0 0' }}>
              {TERRAIN_WORKER_MEMORY_SETTING}
            </Typography.Paragraph>
          </div>
          <div>
            <Typography.Text>{t('terrain.bakeLimits.degradedCommand')}</Typography.Text>
            <Typography.Paragraph code copyable style={{ margin: '4px 0 0' }}>
              {TERRAIN_WORKER_APPLY_COMMAND}
            </Typography.Paragraph>
          </div>
        </Flex>
      }
    />
  );
}

/**
 * What a build stopped at the time limit is told: how long it ran against the limit, and how to
 * allow longer. The two numbers come from the server; without them the heading still says which
 * limit it was.
 */
export function TerrainBakeTimedOut({ build }: { build: TerrainBuild }) {
  const { t } = useTranslation();
  const limit = build.bakeTimeLimit;

  return (
    <Alert
      type="warning"
      showIcon
      data-testid="terrain-build-timed-out"
      title={
        limit
          ? t('terrain.bakeLimits.timedOutTitle', {
              ran: hoursOf(limit.elapsedSeconds),
              limit: hoursOf(limit.limitSeconds),
            })
          : t('terrain.bakeLimits.timedOutTitleBare')
      }
      description={
        <Flex vertical gap={8}>
          <Typography.Text>{t('terrain.bakeLimits.timedOutBody')}</Typography.Text>
          <Typography.Paragraph code copyable style={{ margin: 0 }}>
            {TERRAIN_BAKE_TIMEOUT_SETTING}
          </Typography.Paragraph>
        </Flex>
      }
    />
  );
}
