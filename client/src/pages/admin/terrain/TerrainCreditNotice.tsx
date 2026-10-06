// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMapConfig } from '../../../api/hooks.ts';
import { TERRAIN_ATTRIBUTION_SETTING } from './terrainBuild.ts';

/**
 * Says, above the builds, that the ground the scene is drawing carries no credit.
 *
 * Elevation data almost always has to be credited, and the scene shows whatever credit the drawn
 * terrain carries — so terrain with none is drawn with nothing beside it, which looks exactly like
 * terrain that needs none. Nothing refuses that state and nothing else reports it, so this is the
 * one place it is said, to the people who can put it right.
 *
 * The two origins are put right in two different places, and the notice names the one that
 * applies. A terrain address named in the configuration is credited by a line beside it in the
 * same configuration, so the line is written out to be pasted. A build is credited by the sources
 * it was made from, each of which states its credit when the build is started. The application
 * itself never starts a build without one — every source is refused unless it names a credit —
 * so a build in this state was recorded some other way; it is said all the same, because the
 * scene draws that build's ground uncredited whichever way it came to be so.
 *
 * Silent when no terrain is drawn at all, and when the drawn terrain has a credit.
 */
export default function TerrainCreditNotice() {
  const { t } = useTranslation();
  const { data } = useMapConfig();

  const drawn = data?.terrain;
  if (!drawn || (drawn.attribution ?? '').trim() !== '') {
    return null;
  }

  return (
    <Alert
      type="warning"
      showIcon
      data-testid="terrain-no-credit"
      title={t('terrain.credit.title')}
      description={
        drawn.origin === 'configured' ? (
          <Flex vertical gap={8}>
            <Typography.Text>{t('terrain.credit.configuredBody')}</Typography.Text>
            <div>
              <Typography.Text>{t('terrain.credit.setting')}</Typography.Text>
              <Typography.Paragraph code copyable style={{ margin: '4px 0 0' }}>
                {TERRAIN_ATTRIBUTION_SETTING}
              </Typography.Paragraph>
            </div>
          </Flex>
        ) : (
          <Typography.Text>
            {t('terrain.credit.buildBody', { build: (drawn.buildId ?? '').slice(0, 8) })}
          </Typography.Text>
        )
      }
    />
  );
}
