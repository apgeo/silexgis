// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { Alert, Button, Modal, Space, Tabs } from 'antd';
import {
  BorderOutlined,
  ColumnHeightOutlined,
  LinkOutlined,
  PictureOutlined,
} from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import { type SurveyModelInfo } from '../../api/hooks.ts';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import type { PickedModelPart } from '../../caveview/modelParts.ts';
import { useStationMedia } from '../../caveview/useStationMedia.ts';
import RasterMapPane from '../../rastermap/RasterMapPane.tsx';
import { rasterMapsFromLinks } from '../../rastermap/rasterMaps.ts';
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
  const [picked, setPicked] = useState<PickedModelPart | null>(null);
  const [linking, setLinking] = useState(false);
  const [activeTab, setActiveTab] = useState(TAB_3D);

  // A different model's parts are not this one's, and a closed viewer has nothing selected.
  useEffect(() => {
    setPicked(null);
    setLinking(false);
    setActiveTab(TAB_3D);
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
        <Space direction="vertical" size="small" style={{ width: '100%' }}>
          {picked !== null && (
            <Alert
              type="info"
              showIcon
              message={t(`caveview.picked.${picked.anchorKind}`, { name: picked.label })}
              action={
                <Button size="small" icon={<LinkOutlined />} onClick={() => setLinking(true)}>
                  {t('caveview.linkThisPart')}
                </Button>
              }
              closable
              onClose={() => setPicked(null)}
            />
          )}
          <Tabs
            activeKey={maps.length === 0 ? TAB_3D : activeTab}
            onChange={setActiveTab}
            // While the model declares no maps the strip would be one tab of chrome saying
            // nothing — so the bar is hidden rather than the Tabs left out of the tree,
            // because taking the Tabs out when the declarations arrive would remount the
            // 3D pane and throw away the parsed model with its drawing context.
            tabBarStyle={maps.length === 0 ? { display: 'none' } : undefined}
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
                    onPartPick={setPicked}
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
                  />
                ),
              })),
            ]}
          />
        </Space>
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
