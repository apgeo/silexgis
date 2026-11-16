// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { LANDFORM_CLASSES, LANDFORM_COLOURS } from '../../map/landformClasses.ts';

/**
 * The ten landform classes and the colour each is painted, shown under a landform picture that is
 * on the map: a categorical picture with no key is ten colours nobody can name.
 */
export default function LandformLegend() {
  const { t } = useTranslation();
  return (
    <div
      data-testid="landform-legend"
      style={{ flexBasis: '100%', display: 'flex', flexWrap: 'wrap', gap: '2px 10px', paddingLeft: 24 }}
    >
      {LANDFORM_CLASSES.map((name) => (
        <span key={name} style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
          <span
            aria-hidden
            style={{
              width: 10,
              height: 10,
              borderRadius: 2,
              background: LANDFORM_COLOURS[name],
              border: '1px solid rgba(0, 0, 0, 0.25)',
            }}
          />
          <Typography.Text style={{ fontSize: 12 }}>{t(`terrain.landforms.${name}`)}</Typography.Text>
        </span>
      ))}
    </div>
  );
}
