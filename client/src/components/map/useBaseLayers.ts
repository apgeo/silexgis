// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { unByKey } from 'ol/Observable';
import type { useMapLayers } from '../../api/hooks.ts';
import { getBaseLayerId, getBaseLayers, syncBaseLayers } from '../../map/baseLayers.ts';
import { getWorkspaceMap } from '../../map/mapContext.ts';

/**
 * The page's base layer: which catalogue entry is active (defaulting to the catalogue's own
 * default once it is read), the OpenLayers layers made for it, their per-layer opacity, and the
 * active entry following the on-canvas chooser.
 */
export function useBaseLayers(
  layers: ReturnType<typeof useMapLayers>['data'],
  language: string,
  baseOpacity: Record<number, number>,
) {
  const [activeBaseId, setActiveBaseId] = useState<number>();
  useEffect(() => {
    if (layers && activeBaseId === undefined) {
      const initial = layers.find((l) => l.isDefault && l.isBase) ?? layers.find((l) => l.isBase);
      if (initial) {
        setActiveBaseId(Number(initial.id));
      }
    }
  }, [layers, activeBaseId]);

  // Base layers are created lazily from the catalog; the version bump tells the
  // on-canvas chooser (which needs the OL layer instances) that they exist now.
  const [baseLayersVersion, setBaseLayersVersion] = useState(0);
  useEffect(() => {
    if (layers && activeBaseId !== undefined) {
      syncBaseLayers(getWorkspaceMap(), layers, activeBaseId, language);
      setBaseLayersVersion((v) => v + 1);
    }
  }, [layers, activeBaseId, language]);
  const baseOlLayers = useMemo(
    () => (baseLayersVersion > 0 ? getBaseLayers(getWorkspaceMap()) : []),
    [baseLayersVersion],
  );

  // Base tile layers carry per-catalog-id opacity. Only the active base is visible,
  // but each keeps its own value so switching restores it; re-applied when the base
  // layers are (re)created (version bump) or a saved view changes the opacities.
  useEffect(() => {
    for (const layer of baseOlLayers) {
      const id = getBaseLayerId(layer);
      if (id !== undefined) {
        layer.setOpacity(baseOpacity[id] ?? 1);
      }
    }
  }, [baseOlLayers, baseOpacity]);

  // The on-canvas chooser switches base layers by flipping OL visibility itself;
  // follow it so the radio, saved views and the URL state stay truthful.
  useEffect(() => {
    const keys = baseOlLayers.map((layer) =>
      layer.on('change:visible', () => {
        if (layer.getVisible()) {
          const id = getBaseLayerId(layer);
          if (id !== undefined) {
            setActiveBaseId(id);
          }
        }
      }),
    );
    return () => unByKey(keys);
  }, [baseOlLayers]);

  return { activeBaseId, setActiveBaseId, baseOlLayers };
}
