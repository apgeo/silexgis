// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Alert, App, AutoComplete, Button, Flex, Modal, Space, Tabs, Tooltip } from 'antd';
import {
  BorderOutlined,
  ColumnHeightOutlined,
  LinkOutlined,
  PictureOutlined,
  PlusOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { type SurveyModelInfo } from '../../api/hooks.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import type { PickedModelPart } from '../../caveview/modelParts.ts';
import { useStationMedia } from '../../caveview/useStationMedia.ts';
import { coarsePointer } from '../../map/pointer.ts';
import type { ArmedStation } from '../../rastermap/authoring.ts';
import DeclareMapModal from '../../rastermap/DeclareMapModal.tsx';
import RasterMapPane from '../../rastermap/RasterMapPane.tsx';
import { rasterMapsFromLinks, type RasterMapDeclaration } from '../../rastermap/rasterMaps.ts';
import { useRasterMapLinks } from '../../rastermap/useRasterMapLinks.ts';
import type { MapViewKind } from '../../rastermap/vocabulary.ts';
import AddMemberModal from '../reslinks/AddMemberModal.tsx';
import CaveViewPanel from './CaveViewPanel.tsx';

interface SurveyModelViewerModalProps {
  /** The model to show, or null for a closed modal. */
  model: SurveyModelInfo | null;
  onClose(): void;
}

/** The key of the one tab that is not a map. */
const TAB_3D = '3d';

const VIEW_KIND_ICONS: Record<MapViewKind, ReactNode> = {
  plan: <BorderOutlined />,
  profile: <ColumnHeightOutlined />,
  other: <PictureOutlined />,
};

/**
 * A survey model over the whole window, mounted by whichever page the viewer asked from.
 *
 * Shared rather than copied because two pages now offer it — the cave's own model list, and the
 * map — and because what it carries is not layout but three rules that have to agree: the file
 * name the parser is chosen by, that the viewer is destroyed on close rather than left holding a
 * drawing context for a window nobody is looking at, and that the model is only ever passed in
 * already checked as readable.
 *
 * `destroyOnHidden` is the load-bearing prop. A survey viewer keeps a WebGL context, and a browser
 * keeps only a handful of those alive before it silently drops the oldest — so a hidden-but-mounted
 * viewer is not merely idle, it is one of a small budget that the map and the 3D scene are also
 * drawing from.
 *
 * <b>The model's declared raster maps sit beside the 3D pane as tabs, and the tab strip never
 * remounts the viewer.</b> The Tabs element is always in the tree — its bar is merely hidden
 * while the model declares no maps — so the 3D pane's place does not change when the link read
 * lands and tabs appear; and inactive panes stay mounted (antd's default), so switching to a
 * map and back finds the viewer exactly where it was, holding its parsed model and its WebGL
 * context, rather than parsing everything again. The map panes are Canvas-2D and spend no WebGL
 * context of their own.
 *
 * <b>Linking a part of the survey starts here, from the click that selected it.</b> Which station
 * or which stretch of passage somebody means is a question only the survey can answer, so it is
 * answered by pointing at it in the survey rather than by composing an anchor in a form — the
 * same shape as selecting a passage of a text before linking it. What was picked stays on offer
 * until something else is picked, because a click that immediately opened a dialog would make
 * looking around the model impossible.
 */
export default function SurveyModelViewerModal({ model, onClose }: SurveyModelViewerModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [picked, setPicked] = useState<PickedModelPart | null>(null);
  const [linking, setLinking] = useState(false);
  const [activeTab, setActiveTab] = useState(TAB_3D);
  /** The map (by declaration link id) whose points are being defined, or null. */
  const [defining, setDefining] = useState<string | null>(null);
  /** The station the next map click will pin. */
  const [armed, setArmed] = useState<ArmedStation | null>(null);
  /** What the typeahead currently holds — controlled so a chosen station clears it. */
  const [stationQuery, setStationQuery] = useState('');
  /** Every station of the loaded drawing, in the viewer's own spelling. */
  const [stationIndex, setStationIndex] = useState<readonly string[]>([]);
  const [declaring, setDeclaring] = useState(false);
  const [editingMap, setEditingMap] = useState<RasterMapDeclaration | null>(null);

  // A different model's parts are not this one's, and a closed viewer has nothing selected.
  useEffect(() => {
    setPicked(null);
    setLinking(false);
    setActiveTab(TAB_3D);
    setDefining(null);
    setArmed(null);
    setStationQuery('');
    setStationIndex([]);
    setDeclaring(false);
    setEditingMap(null);
  }, [model?.id]);

  // The photographs somebody has already linked to stations of this model, shown over the model
  // where they were taken. Asked for only while the viewer is open, because that is the only time
  // anything is drawn from them.
  const stationMedia = useStationMedia(model?.id, model !== null);

  // The model's incident links, read once for the whole tab set: the map declarations fold
  // out here, and each map pane folds its own pins from the same answer.
  const { data: links } = useRasterMapLinks(model?.id, model !== null);
  const maps = useMemo(
    () => rasterMapsFromLinks(links ?? [], model?.id ?? ''),
    [links, model?.id],
  );

  // A tab that stops existing — its map undeclared — cannot stay active, and neither can
  // a define mode pointed at it.
  useEffect(() => {
    if (activeTab !== TAB_3D && !maps.some((map) => map.linkId === activeTab)) {
      setActiveTab(TAB_3D);
    }
    if (defining !== null && !maps.some((map) => map.linkId === defining)) {
      setDefining(null);
      setArmed(null);
      setStationQuery('');
    }
  }, [maps, activeTab, defining]);

  // Tab-away ends the paired mode — except for the 3D pane, which is half of it: arming
  // by pressing a station happens there, so the round trip 3D ↔ the defining map keeps
  // both the mode and the armed station.
  useEffect(() => {
    if (defining !== null && activeTab !== defining && activeTab !== TAB_3D) {
      setDefining(null);
      setArmed(null);
      setStationQuery('');
    }
  }, [activeTab, defining]);

  // Escape disarms — and only disarms. Captured before antd's own listener, which would
  // otherwise read the same press as "close the viewer": one Escape, one undo.
  useEffect(() => {
    if (armed === null) {
      return;
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        setArmed(null);
      }
    };
    document.addEventListener('keydown', onKeyDown, true);
    return () => document.removeEventListener('keydown', onKeyDown, true);
  }, [armed]);

  /**
   * A pick in the 3D pane: in define mode it arms — a station is the only thing a pin
   * can claim, so a survey or a leg is refused with the reason — and outside define mode
   * it is the ordinary link-this-part offer.
   */
  const handlePick = (part: PickedModelPart) => {
    if (defining !== null) {
      if (part.anchorKind === 'modelStation') {
        setArmed({ station: part.anchor.station, replaceLinkId: null });
      } else {
        message.info(t('rastermap.pickSingleStation'));
      }
      return;
    }
    setPicked(part);
  };

  const stationOptions = useMemo(() => {
    const query = stationQuery.trim().toLowerCase();
    if (query.length === 0) {
      return [];
    }
    return stationIndex
      .filter((station) => station.toLowerCase().includes(query))
      .slice(0, 20)
      .map((station) => ({ value: station }));
  }, [stationIndex, stationQuery]);

  // Finger-driven authoring gets finger-sized controls — same axis as the hit tolerances.
  const controlSize = coarsePointer() ? 'large' : 'middle';

  return (
    <Modal
      title={model?.name}
      open={model !== null}
      onCancel={onClose}
      footer={null}
      width="min(1200px, 95vw)"
      destroyOnHidden
    >
      {model && (
        <Space orientation="vertical" size="small" style={{ width: '100%' }}>
          {picked !== null && defining === null && (
            <Alert
              type="info"
              showIcon
              title={t(`caveview.picked.${picked.anchorKind}`, { name: picked.label })}
              action={
                <Button size="small" icon={<LinkOutlined />} onClick={() => setLinking(true)}>
                  {t('caveview.linkThisPart')}
                </Button>
              }
              closable
              onClose={() => setPicked(null)}
            />
          )}
          {defining !== null && (
            // The paired mode's own banner, above the strip so it stays on screen — and
            // reachable — on whichever tab the reader is on, at phone widths included:
            // armed shows what the next map click will pin, unarmed offers the two ways
            // to arm (press a station in 3D, or find one by name here).
            <Alert
              type="info"
              data-testid="rastermap-authoring"
              title={
                armed !== null ? (
                  <Flex wrap gap={8} align="center">
                    <span data-testid="rastermap-armed">
                      {t('rastermap.armedStation', { station: armed.station })}
                    </span>
                    <Button
                      size={controlSize}
                      data-testid="rastermap-disarm"
                      onClick={() => setArmed(null)}
                    >
                      {t('common.cancel')}
                    </Button>
                  </Flex>
                ) : (
                  <Flex wrap gap={8} align="center">
                    <AutoComplete
                      value={stationQuery}
                      options={stationOptions}
                      onSearch={setStationQuery}
                      onSelect={(station: string) => {
                        setArmed({ station, replaceLinkId: null });
                        setStationQuery('');
                      }}
                      placeholder={t('rastermap.stationPlaceholder')}
                      aria-label={t('rastermap.stationPlaceholder')}
                      data-testid="rastermap-station-search"
                      size={controlSize}
                      style={{ minWidth: 200 }}
                    />
                    <span>{t('rastermap.armHint')}</span>
                  </Flex>
                )
              }
            />
          )}
          <Tabs
            activeKey={maps.length === 0 ? TAB_3D : activeTab}
            onChange={setActiveTab}
            // The strip now always shows: even with no maps it carries the one action
            // that gets a model its first map. The Tabs element stays in the tree either
            // way, so the 3D pane's place never changes when declarations arrive.
            tabBarExtraContent={
              <Tooltip title={t('rastermap.addMap')}>
                <Button
                  type="text"
                  icon={<PlusOutlined />}
                  aria-label={t('rastermap.addMap')}
                  data-testid="rastermap-add-map"
                  onClick={() => setDeclaring(true)}
                />
              </Tooltip>
            }
            items={[
              {
                key: TAB_3D,
                label: t('rastermap.tab3d'),
                children: (
                  <CaveViewPanel
                    fileUrl={model.modelUrl}
                    fileName={viewerFileName(model)}
                    height="70vh"
                    surveyModelId={model.id}
                    onPartPick={handlePick}
                    onStationsLoaded={setStationIndex}
                    // The window given over to the model is where the viewer's own controls
                    // belong; the docked panels elsewhere have chrome of their own competing
                    // for the same corner.
                    toolbar
                    stationMedia={stationMedia}
                  />
                ),
              },
              ...maps.map((declaration) => ({
                key: declaration.linkId,
                label: (
                  <span>
                    {VIEW_KIND_ICONS[declaration.viewKind]}{' '}
                    {declaration.title ?? t('rastermap.untitledMap')}
                  </span>
                ),
                children: (
                  <RasterMapPane
                    declaration={declaration}
                    links={links ?? []}
                    surveyModelId={model.id}
                    active={activeTab === declaration.linkId}
                    height="70vh"
                    authoring={{
                      defining: defining === declaration.linkId,
                      onToggleDefining: () => {
                        setArmed(null);
                        setStationQuery('');
                        if (defining === declaration.linkId) {
                          setDefining(null);
                        } else {
                          setDefining(declaration.linkId);
                          // The paired mode takes over picks; a leftover offer would
                          // stand beside the banner claiming the same click.
                          setPicked(null);
                        }
                      },
                      armed,
                      onArm: setArmed,
                      onDisarm: () => setArmed(null),
                      onEditDeclaration: () => setEditingMap(declaration),
                    }}
                  />
                ),
              })),
            ]}
          />
        </Space>
      )}
      {model && (
        <DeclareMapModal
          open={declaring}
          onClose={() => setDeclaring(false)}
          surveyModelId={model.id}
        />
      )}
      {model && (
        <DeclareMapModal
          open={editingMap !== null}
          onClose={() => setEditingMap(null)}
          surveyModelId={model.id}
          editing={editingMap}
        />
      )}
      {model && picked !== null && (
        <AddMemberModal
          open={linking}
          onClose={() => setLinking(false)}
          origin={{
            targetType: 'surveyModel',
            targetId: model.id,
            title: model.name,
            // The part that was clicked, not the model as a whole. Composed here rather than in
            // the dialog because it was chosen by pointing at it, which has already happened.
            anchor: { anchorKind: picked.anchorKind, anchor: picked.anchor },
          }}
          onCreated={() => {
            setLinking(false);
            setPicked(null);
          }}
        />
      )}
    </Modal>
  );
}
