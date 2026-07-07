// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Group, Panel, Separator } from 'react-resizable-panels';
import { useMapLayers } from '../api/hooks.ts';
import LayerPanel from '../components/map/LayerPanel.tsx';
import MapSearch from '../components/map/MapSearch.tsx';
import SelectionPanel from '../components/map/SelectionPanel.tsx';
import { setActiveBaseLayer, syncBaseLayers } from '../map/baseLayers.ts';
import { ENTRANCE_LAYER_ID, attachEntranceLoader, createEntranceLayer } from '../map/entranceLayer.ts';
import { getWorkspaceMap } from '../map/mapContext.ts';
import { attachSelection } from '../map/selection.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import './MapPage.css';

/** Map workspace v1: fixed resizable panes; docking comes later. */
export default function MapPage() {
  const mapTarget = useRef<HTMLDivElement>(null);
  const { data: layers } = useMapLayers();
  const [activeBaseId, setActiveBaseId] = useState<number>();
  const [entrancesVisible, setEntrancesVisible] = useState(true);
  const setSelection = useWorkspaceStore((s) => s.setSelection);

  useEffect(() => {
    const map = getWorkspaceMap();
    map.setTarget(mapTarget.current ?? undefined);

    if (!map.getLayers().getArray().some((l) => l.get('id') === ENTRANCE_LAYER_ID)) {
      map.addLayer(createEntranceLayer());
    }

    const detachLoader = attachEntranceLoader(map);
    const detachSelection = attachSelection(map, setSelection);
    return () => {
      detachLoader();
      detachSelection();
      map.setTarget(undefined);
    };
  }, [setSelection]);

  useEffect(() => {
    if (layers && activeBaseId === undefined) {
      const initial = layers.find((l) => l.isDefault && l.isBase) ?? layers.find((l) => l.isBase);
      if (initial) {
        setActiveBaseId(Number(initial.id));
      }
    }
  }, [layers, activeBaseId]);

  useEffect(() => {
    if (layers && activeBaseId !== undefined) {
      syncBaseLayers(getWorkspaceMap(), layers, activeBaseId);
    }
  }, [layers, activeBaseId]);

  useEffect(() => {
    const layer = getWorkspaceMap()
      .getLayers()
      .getArray()
      .find((l) => l.get('id') === ENTRANCE_LAYER_ID);
    layer?.setVisible(entrancesVisible);
  }, [entrancesVisible]);

  return (
    <Group orientation="horizontal" className="map-workspace">
      <Panel defaultSize={16} minSize={10} className="map-workspace-panel">
        <LayerPanel
          layers={layers ?? []}
          activeBaseId={activeBaseId}
          onBaseChange={(id) => {
            setActiveBaseId(id);
            setActiveBaseLayer(getWorkspaceMap(), id);
          }}
          entrancesVisible={entrancesVisible}
          onEntrancesVisibleChange={setEntrancesVisible}
        />
      </Panel>
      <Separator className="map-workspace-handle" />
      <Panel minSize={30}>
        <div className="map-canvas-wrap">
          <div ref={mapTarget} className="map-canvas" data-testid="map-canvas" />
          <div className="map-search-overlay">
            <MapSearch />
          </div>
        </div>
      </Panel>
      <Separator className="map-workspace-handle" />
      <Panel defaultSize={22} minSize={12} className="map-workspace-panel">
        <SelectionPanel />
      </Panel>
    </Group>
  );
}
