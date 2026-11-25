// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { CAVE_POTENTIAL_STOPS } from '../../map/cavePotentialLayers.ts';

/**
 * The key to a cave-potential map that is on the map, and what the map is not.
 *
 * The ramp is painted from the same stops the layer is coloured with. The sentence under it is
 * there because a coloured map of "where caves may be" is read as a finding: it is the weighing
 * its model describes, of the evidence the run could read, and nothing has been checked on the
 * ground. A map made from recorded positions says so, since it is shown only to readers who may
 * place them and must not be passed on.
 */
export default function CavePotentialLegend({
  usesPositions,
  relative = false,
}: {
  usesPositions: boolean;
  /**
   * Whether the map's values were stretched between its own least and greatest, as a map learnt
   * from known entrances is: its colours then rank the ground of this one map and are not a score
   * that can be set beside another map's.
   */
  relative?: boolean;
}) {
  const { t } = useTranslation();
  const ramp = `linear-gradient(to right, ${CAVE_POTENTIAL_STOPS.map(
    ([at, colour]) => `${colour} ${at * 100}%`,
  ).join(', ')})`;

  return (
    <div data-testid="cave-potential-legend" style={{ flexBasis: '100%', paddingLeft: 24 }}>
      <div
        aria-hidden
        style={{ height: 10, borderRadius: 2, background: ramp, border: '1px solid rgba(0, 0, 0, 0.25)' }}
      />
      <div style={{ display: 'flex', justifyContent: 'space-between' }}>
        <Typography.Text className="hint-text">{t('cavePotential.legend.low')}</Typography.Text>
        <Typography.Text className="hint-text">{t('cavePotential.legend.high')}</Typography.Text>
      </div>
      <Typography.Text type="secondary" className="hint-text" data-testid="cave-potential-caveat">
        {t('cavePotential.legend.caveat')}
        {relative ? ` ${t('cavePotential.legend.relativeCaveat')}` : ''}
        {usesPositions ? ` ${t('cavePotential.legend.positionsCaveat')}` : ''}
      </Typography.Text>
    </div>
  );
}
