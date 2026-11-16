// SPDX-License-Identifier: AGPL-3.0-or-later
import { Typography } from 'antd';
import { unByKey } from 'ol/Observable';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { getSurfaceFeatureSource } from '../../map/featureLayer.ts';
import { lithologyAppearance } from '../../map/lithologyAppearance.ts';
import { lithologyLabel } from './lithology.ts';
import { type LithologyKeyEntry, lithologyKeyOf } from './lithologyKey.ts';

/**
 * The key to the geological units on the map: a swatch per class actually drawn in the loaded
 * extent, so it is as long as the map is varied and absent when no unit is loaded. It reads the
 * features layer's loaded rows rather than asking the server again — the colours belong to what
 * is on screen, not to the registry.
 */
export default function LithologyLegend() {
  const { t } = useTranslation();
  const [entries, setEntries] = useState<LithologyKeyEntry[]>(() => lithologyKeyOf(getSurfaceFeatureSource()));

  useEffect(() => {
    const source = getSurfaceFeatureSource();
    let timer: number | undefined;
    // A reload adds rows one change event at a time; the key is worked out once it settles.
    const key = source.on('change', () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => setEntries(lithologyKeyOf(source)), 200);
    });
    return () => {
      unByKey(key);
      window.clearTimeout(timer);
    };
  }, []);

  if (entries.length === 0) return null;

  return (
    <div style={{ marginTop: 8 }} role="group" aria-label={t('geology.legend.title')} data-testid="lithology-legend">
      <Typography.Text strong style={{ fontSize: 12 }}>
        {t('geology.legend.title')}
      </Typography.Text>
      <ul style={{ listStyle: 'none', margin: '4px 0 0', padding: 0 }}>
        {entries.map((entry) => {
          const look = lithologyAppearance(entry.lithology, entry.karstifiable);
          return (
            <li
              key={entry.lithology ?? ''}
              data-standing={look.standing}
              style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 12, lineHeight: '20px' }}
            >
              <span
                aria-hidden
                style={{
                  display: 'inline-block',
                  width: 18,
                  height: 12,
                  flex: 'none',
                  background: look.fill ?? 'transparent',
                  border: `${Math.max(1, Math.round(look.width))}px ${look.lineDash ? 'dashed' : 'solid'} ${look.stroke}`,
                }}
              />
              <span>
                {lithologyLabel(t, entry.lithology)}
                {look.standing === 'karstifiable' && (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {' '}
                    · {t('geology.karstifiable')}
                  </Typography.Text>
                )}
                {look.standing === 'unrecognised' && (
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    {' '}
                    · {t('geology.unrecognised')}
                  </Typography.Text>
                )}
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
