// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Card, Descriptions, Empty, Flex, Progress, Skeleton, Steps, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useTerrainBuild } from '../../../api/hooks.ts';
import type { TerrainBuildSource } from '../../../api/hooks.ts';
import { TERRAIN_PHASE_ORDER, bakeWorkerMissing, phaseIndex } from './terrainBuild.ts';
import TerrainWorkerMissing from './TerrainWorkerMissing.tsx';

interface Props {
  buildId: string;
}

/**
 * What a build is doing, for a run that takes eleven seconds and for one that takes hours.
 *
 * The pipeline is drawn as the steps themselves, each carrying its own state, so a run that
 * stopped early leaves the steps after it plainly unvisited instead of shifting the layout, and a
 * step the build passed through without pausing still reads as done. The bar shows the number the
 * server computed over the whole chain and nothing animates on its own: an animation is a promise
 * about how long something takes, and this makes no such promise.
 *
 * The tail of the tool's own output is here because it is the only place a person can see why a
 * bake is slow rather than stuck. It arrives with this response alone, which is why watching a
 * build costs a request of its own rather than being read out of the list.
 */
export default function TerrainBuildProgress({ buildId }: Props) {
  const { t } = useTranslation();
  const { data, isLoading } = useTerrainBuild(buildId);

  if (isLoading || !data) {
    return (
      <Card size="small" data-testid="terrain-build-detail">
        <Skeleton active paragraph={{ rows: 3 }} />
      </Card>
    );
  }

  const { build, logTail, sources } = data;
  const current = phaseIndex(build.phase);
  const failed = build.status === 'failed';
  const succeeded = build.status === 'succeeded';

  return (
    <Card size="small" title={t('terrain.detail.title')} data-testid="terrain-build-detail">
      <Flex vertical gap={16}>
        <Steps
          size="small"
          data-testid="terrain-pipeline"
          items={TERRAIN_PHASE_ORDER.map((phase, index) => ({
            title: <span data-testid={`terrain-phase-${phase}`}>{t(`terrain.phases.${phase}`)}</span>,
            status:
              index < current
                ? 'finish'
                : index > current
                  ? 'wait'
                  : failed
                    ? 'error'
                    : succeeded
                      ? 'finish'
                      : 'process',
          }))}
        />

        {build.phase === 'pending' && (
          <Typography.Text type="secondary" data-testid="terrain-phase-pending">
            {t('terrain.phases.pending')}
          </Typography.Text>
        )}

        <Progress
          percent={build.progress}
          data-testid="terrain-build-progress"
          // Never "active": that animation says a thing is moving at a rate, and this bar covers
          // both a build that finishes before it is looked at and one that runs overnight.
          status={failed ? 'exception' : succeeded ? 'success' : 'normal'}
        />

        {build.message !== null && !failed && (
          <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
            {build.message}
          </Typography.Paragraph>
        )}

        {/*
          A build that stopped for want of anything to make tiles with is not a failure of that
          build: this installation simply does not run that service, which is what a plain one
          does. It gets the explanation of the state rather than the red alarm, in the reader's own
          language rather than in the server's English. What to type to enable the service is said
          once, at the top of the page, where somebody about to draw another rectangle reads it —
          repeating it here would put the same alert on the screen twice.
        */}
        {failed && bakeWorkerMissing(build) && <TerrainWorkerMissing compact />}

        {failed && !bakeWorkerMissing(build) && (
          <Alert
            type="error"
            showIcon
            data-testid="terrain-build-failure"
            message={t('terrain.detail.failed')}
            // The server writes this one in words for whoever has to act on it, and it says more
            // than any code can — what it was reading, which step it stopped at, what to do next.
            description={build.message ?? undefined}
          />
        )}

        <Descriptions size="small" column={1} data-testid="terrain-build-sources">
          <Descriptions.Item label={t('terrain.detail.sources')}>
            {sources.length === 0 ? (
              <Typography.Text type="secondary">{t('terrain.detail.noSources')}</Typography.Text>
            ) : (
              <Flex vertical gap={2}>
                {sources.map((source: TerrainBuildSource) => (
                  <Typography.Text key={source.id}>
                    {t(`terrain.sourceKinds.${source.kind}`)} · {source.reference} ·{' '}
                    {source.attribution}
                  </Typography.Text>
                ))}
              </Flex>
            )}
          </Descriptions.Item>
        </Descriptions>

        <div>
          <Typography.Text strong>{t('terrain.detail.log')}</Typography.Text>
          {logTail === null || logTail.length === 0 ? (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={t('terrain.detail.noLog')}
            />
          ) : (
            <pre
              data-testid="terrain-log-tail"
              style={{
                margin: '8px 0 0',
                fontSize: 12,
                whiteSpace: 'pre-wrap',
                maxHeight: 240,
                overflow: 'auto',
              }}
            >
              {logTail}
            </pre>
          )}
        </div>
      </Flex>
    </Card>
  );
}
