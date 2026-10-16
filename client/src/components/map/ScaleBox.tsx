// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { AutoComplete, Input } from 'antd';
import MapUtil from '@terrestris/ol-util/dist/MapUtil/MapUtil';
import type Map from 'ol/Map';
import type { Units } from 'ol/proj/Units';
import { useTranslation } from 'react-i18next';
import { formatScale, parseScale } from '../../map/scale.ts';

/** The map's scale as the standard pixel reckons it, rounded the way scale lists round. */
function scaleOf(map: Map): number | undefined {
  const view = map.getView();
  const resolution = view.getResolution();
  if (resolution === undefined) {
    return undefined;
  }
  const scale = MapUtil.getScaleForResolution(resolution, view.getProjection().getUnits() as Units);
  return scale === undefined ? undefined : MapUtil.roundScale(scale);
}

/**
 * The map's scale, which can be typed into: `1:25000`, `25000` or `25 000`, then Enter, and the
 * map goes to that scale. The scales of the map's zoom steps are offered as a list to pick from,
 * as the mapping library's own scale picker offered them — which stopped there: a scale not on
 * that list could not be had.
 *
 * Text that is not a scale is refused where it was typed, with the reason under the box, and the
 * map is left where it was.
 */
export default function ScaleBox({ map }: { map: Map }) {
  const { t, i18n } = useTranslation();
  const errorId = useId();
  const locale = i18n.resolvedLanguage;
  const [text, setText] = useState(() => {
    const scale = scaleOf(map);
    return scale === undefined ? '' : formatScale(scale, locale);
  });
  const [editing, setEditing] = useState(false);
  const [invalid, setInvalid] = useState(false);
  // Enter on a highlighted entry of the list picks that entry; the same Enter also reaches the
  // box, which must then not commit the half-typed text over the pick.
  const picked = useRef(false);

  // Follows the map while nobody is typing; what somebody is typing is never overwritten.
  useEffect(() => {
    const view = map.getView();
    const follow = () => {
      if (!editing) {
        const scale = scaleOf(map);
        setText(scale === undefined ? '' : formatScale(scale, locale));
      }
    };
    follow();
    view.on('change:resolution', follow);
    return () => view.un('change:resolution', follow);
  }, [map, editing, locale]);

  const options = useMemo(() => {
    const view = map.getView();
    const units = view.getProjection().getUnits() as Units;
    const scales = new Set<number>();
    for (let zoom = Math.ceil(view.getMinZoom()); zoom <= Math.floor(view.getMaxZoom()); zoom++) {
      const scale = MapUtil.getScaleForResolution(view.getResolutionForZoom(zoom), units);
      if (scale !== undefined) {
        scales.add(MapUtil.roundScale(scale));
      }
    }
    return [...scales].map((scale) => ({ value: formatScale(scale, locale) }));
  }, [map, locale]);

  const commit = (value: string) => {
    const denominator = parseScale(value);
    if (denominator === null) {
      setInvalid(true);
      return;
    }
    const view = map.getView();
    const resolution = MapUtil.getResolutionForScale(denominator, view.getProjection().getUnits() as Units);
    if (resolution === undefined) {
      setInvalid(true);
      return;
    }
    setInvalid(false);
    setEditing(false);
    view.setResolution(resolution);
    setText(formatScale(denominator, locale));
  };

  return (
    <div className="map-scale-box">
      <AutoComplete
        value={text}
        options={options}
        onChange={(value: string) => {
          setEditing(true);
          setInvalid(false);
          setText(value);
        }}
        onSelect={(value: string) => {
          picked.current = true;
          commit(value);
        }}
        // The whole list stays offered while typing: it is a list of scales to jump to, not a
        // search over them — and nothing in it is highlighted until somebody moves to it, so a
        // typed scale is what Enter takes.
        filterOption={false}
        defaultActiveFirstOption={false}
        popupMatchSelectWidth={false}
        style={{ width: 128 }}
      >
        <Input
          size="small"
          aria-label={t('a11y.mapScale')}
          aria-invalid={invalid}
          aria-describedby={invalid ? errorId : undefined}
          status={invalid ? 'error' : undefined}
          onPressEnter={(event) => {
            const typed = event.currentTarget.value;
            setTimeout(() => {
              if (!picked.current) {
                commit(typed);
              }
              picked.current = false;
            });
          }}
          onBlur={() => {
            if (!invalid) {
              setEditing(false);
            }
          }}
        />
      </AutoComplete>
      {invalid && (
        <div id={errorId} role="alert" className="map-scale-error">
          {t('map.scaleInvalid')}
        </div>
      )}
    </div>
  );
}
