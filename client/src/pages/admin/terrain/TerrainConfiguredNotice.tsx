// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMapConfig } from '../../../api/hooks.ts';
import { TERRAIN_URL_SETTING } from './terrainBuild.ts';

/**
 * Says, above the builds, that a terrain address in the installation's configuration is what the
 * scene draws — not the build chosen here.
 *
 * The server answers the map with two terrain sources: the one it serves, and the one it would
 * serve instead. When the first comes from configuration and the second is a build, an
 * administrator has chosen a build that nothing draws, and nothing refuses the choice: the build
 * list marks it "being drawn" and the scene draws something else. This is the one place that can
 * say so, and it names the build and the exact setting so that the way out can be pasted rather
 * than paraphrased. Silent otherwise: an installation with no configured address, or no chosen
 * build, is in no such state.
 */
export default function TerrainConfiguredNotice() {
  const { t } = useTranslation();
  const { data } = useMapConfig();

  const overridden =
    data?.terrain?.origin === 'configured' ? (data.terrainFallback?.buildId ?? null) : null;
  if (overridden === null) {
    return null;
  }

  return (
    <Alert
      type="warning"
      showIcon
      data-testid="terrain-configured-override"
      title={t('terrain.configured.title')}
      description={
        <Flex vertical gap={8}>
          <Typography.Text>
            {t('terrain.configured.body', { build: overridden.slice(0, 8) })}
          </Typography.Text>
          <div>
            <Typography.Text>{t('terrain.configured.setting')}</Typography.Text>
            <Typography.Paragraph code copyable style={{ margin: '4px 0 0' }}>
              {TERRAIN_URL_SETTING}
            </Typography.Paragraph>
          </div>
        </Flex>
      }
    />
  );
}
