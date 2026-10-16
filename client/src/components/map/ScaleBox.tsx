// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useId, useRef, useState } from 'react';
import { AutoComplete, Input } from 'antd';
import MapUtil from '@terrestris/ol-util/dist/MapUtil/MapUtil';
import type Map from 'ol/Map';
import { getPointResolution } from 'ol/proj';
import { useTranslation } from 'react-i18next';
import { formatScale, parseScale } from '../../map/scale.ts';

/**
 * Ground metres per metre of the map's projection at the middle of the view. In Web Mercator a
 * projected metre is a ground metre only at the equator; at 45° it is about 0.71 of one, so the
 * scale the projection's own units give — the nominal scale — is not the scale a measured distance
 * on the screen has. The scale box works on the ground.
 */
function groundPerProjected(map: Map): number | undefined {
  const view = map.getView();
  const centre = view.getCenter();
  return centre ? getPointResolution(view.getProjection(), 1, centre, 'm') : undefined;
}

/** The ground scale at a resolution, at the middle of the view, as the standard pixel reckons it. */
function groundScaleAt(map: Map, resolution: number | undefined): number | undefined {
  const ratio = groundPerProjected(map);
  if (resolution === undefined || ratio === undefined) {
    return undefined;
  }
  return MapUtil.getScaleForResolution(resolution * ratio, 'm');
}

/** The map's ground scale, rounded the way scale lists round. */
function scaleOf(map: Map): number | undefined {
  const scale = groundScaleAt(map, map.getView().getResolution());
  return scale === undefined ? undefined : MapUtil.roundScale(scale);
}

/**
 * The map's scale on the ground at the middle of the view, which can be typed into: `1:25000`,
 * `25000` or `25 000`, then Enter, and the map goes to that scale — 1:25000 on the ground there,
 * as a printed sheet of that scale would be. The scales of the map's zoom steps are offered as a list to pick from,
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
    // The ground scale changes with the latitude, so panning north or south changes it too.
    view.on(['change:resolution', 'change:center'], follow);
    return () => view.un(['change:resolution', 'change:center'], follow);
  }, [map, editing, locale]);

  // The zoom steps' scales on the ground where the view is now — worked out when the list is drawn,
  // which is when somebody opens it.
  const view = map.getView();
  const scales = new Set<number>();
  for (let zoom = Math.ceil(view.getMinZoom()); zoom <= Math.floor(view.getMaxZoom()); zoom++) {
    const scale = groundScaleAt(map, view.getResolutionForZoom(zoom));
    if (scale !== undefined) {
      scales.add(MapUtil.roundScale(scale));
    }
  }
  const options = [...scales].map((scale) => ({ value: formatScale(scale, locale) }));

  const commit = (value: string) => {
    const denominator = parseScale(value);
    if (denominator === null) {
      setInvalid(true);
      return;
    }
    const ground = MapUtil.getResolutionForScale(denominator, 'm');
    const ratio = groundPerProjected(map);
    if (ground === undefined || ratio === undefined) {
      setInvalid(true);
      return;
    }
    setInvalid(false);
    setEditing(false);
    map.getView().setResolution(ground / ratio);
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
