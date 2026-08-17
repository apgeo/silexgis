// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useTerrainBuilds } from '../../../api/hooks.ts';
import {
  TERRAIN_BAKE_SETTING,
  TERRAIN_PAGE_SIZE,
  TERRAIN_WORKER_COMMAND,
  bakeWorkerMissing,
} from './terrainBuild.ts';

/**
 * What an installation with nothing to make tiles with is told.
 *
 * Deliberately not an error. A plain installation does not run the tile-making service — the image
 * is well over a gigabyte and an installation that never builds terrain should not have to hold
 * it — so this is the common case rather than a fault, and everything before the bake works
 * without it. The words say what is missing, what to type to have it, and that nothing already
 * done is lost, which is the difference between a state somebody can leave and one they are stuck
 * in. The two lines are written out rather than paraphrased because a paraphrase cannot be pasted.
 *
 * Compact drops those two lines and keeps the rest. It is what the build that met this state shows
 * beside its own pipeline, where the page above it is already carrying the full explanation: the
 * same alert twice on one screen makes neither of them read as the answer, and two copyable command
 * blocks four inches apart are a question about which one to use.
 */
export default function TerrainWorkerMissing({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation();

  return (
    <Alert
      type="info"
      showIcon
      data-testid={compact ? 'terrain-build-bake-missing' : 'terrain-worker-missing'}
      message={t('terrain.worker.title')}
      description={
        <Flex vertical gap={8}>
          <Typography.Text>{t('terrain.worker.body')}</Typography.Text>
          {!compact && (
            <>
              <div>
                <Typography.Text>{t('terrain.worker.setting')}</Typography.Text>
                <Typography.Paragraph code copyable style={{ margin: '4px 0 0' }}>
                  {TERRAIN_BAKE_SETTING}
                </Typography.Paragraph>
              </div>
              <div>
                <Typography.Text>{t('terrain.worker.command')}</Typography.Text>
                <Typography.Paragraph code copyable style={{ margin: '4px 0 0' }}>
                  {TERRAIN_WORKER_COMMAND}
                </Typography.Paragraph>
              </div>
            </>
          )}
          <Typography.Text type="secondary">{t('terrain.worker.resumes')}</Typography.Text>
        </Flex>
      }
    />
  );
}

/**
 * The same explanation, raised to the top of the page once any build has met it.
 *
 * It reads the first page of the list, which is the one the list itself opens on, so in the
 * ordinary case this costs no request of its own. Builds come back newest first and a stopped bake
 * stays stopped, so evidence of a missing service is on that first page for as long as it matters;
 * once the service is running and a build gets past the bake, an older stopped one eventually
 * falls off the page and the notice goes with it.
 */
export function TerrainWorkerNotice() {
  const { data } = useTerrainBuilds({ page: 1, pageSize: TERRAIN_PAGE_SIZE });

  if (!(data?.items ?? []).some(bakeWorkerMissing)) {
    return null;
  }

  return <TerrainWorkerMissing />;
}
