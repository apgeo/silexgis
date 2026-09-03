// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Flex, Input, Table, Typography } from 'antd';
import type { TablePaginationConfig } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams, useSearchParams } from 'react-router-dom';
import {
  surveyModelReadableByViewer,
  useCave,
  useCaves,
  useEntrances,
  useSurveyModel,
  useSurveyModels,
  type CaveListItem,
  type CaveListParams,
} from '../../api/hooks.ts';
import CaveViewPanel from '../../components/caveview/CaveViewPanel.tsx';
import { viewerFileName } from '../../caveview/viewerFileName.ts';
import SelectionPanel from '../../components/map/SelectionPanel.tsx';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';
import Scene3DView from '../../components/scene3d/Scene3DView.tsx';
import AnnotatedTextPanel from '../../textlink/AnnotatedTextPanel.tsx';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { publish, subscribe } from '../../workspace/workspaceBus.ts';

/**
 * A single panel rendered chrome-less for pop-out windows (multi-monitor work).
 * Interactions publish references over the workspace bus; the main window's map
 * reacts. Panels: the cave registry, the survey-model viewer and the 3D scene.
 *
 * `viewer3d` and `scene3d` are two different things and neither replaces the other: the first is
 * the survey-model viewer, which reads a cave's own `.lox`/`.3d` file and knows nothing about
 * where in the world it is; the second is the geographic scene, with terrain, basemaps and every
 * cave in view at once. The ids say which is which, and the titles say it to the viewer.
 */
export default function PanelPage() {
  const { t } = useTranslation();
  const { panelId } = useParams<{ panelId: string }>();

  switch (panelId) {
    case 'registry':
      return <RegistryPanel />;
    case 'viewer3d':
      return <Viewer3dPanel />;
    case 'scene3d':
      return <Scene3dScenePanel />;
    case 'selection':
      return <SelectionPopout />;
    case 'text':
      return <TextPopout />;
    default:
      return (
        <Flex align="center" justify="center" style={{ height: '100vh' }}>
          <Typography.Text type="secondary">{t('panel.unknown')}</Typography.Text>
        </Flex>
      );
  }
}

/**
 * One annotated text in a window of its own — the second monitor holding the report while the
 * first holds the map it is about.
 *
 * The document is named in the address rather than followed from the bus, which is the opposite
 * of what the other pop-outs do and is right for this one: the others watch whatever is selected,
 * while this window was opened to read one particular text and must go on showing it while the
 * reader clicks through everything it links to.
 *
 * Its view controls are whatever this window has, which is none — so following a link from here
 * reaches the map in the window it was opened from, over the bus. That is the arrangement the
 * cross-window roster exists for.
 */
function TextPopout() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const documentId = params.get('document');

  if (documentId === null) {
    return (
      <Flex align="center" justify="center" style={{ height: '100vh' }}>
        <Typography.Text type="secondary">{t('panel.unknown')}</Typography.Text>
      </Flex>
    );
  }

  return (
    <div style={{ height: '100vh' }}>
      <AnnotatedTextPanel documentId={documentId} mayPopOut={false} />
    </div>
  );
}

/**
 * The selection panel in a window of its own — the arrangement somebody sets up on a second
 * monitor to watch one object while working on the map in the first.
 *
 * It follows selections over the bus rather than sharing a store, because a pop-out is a separate
 * window with its own copy of every module. Its arrangement is its own too: this window was opened
 * for a purpose, and inheriting the main window's sections would defeat that.
 */
function SelectionPopout() {
  const { t } = useTranslation();
  const setSelection = useWorkspaceStore((s) => s.setSelection);

  useEffect(
    () =>
      subscribe((event) => {
        if (event.kind === 'selection') {
          setSelection(event.selection ?? null);
        }
      }),
    [setSelection],
  );

  return (
    <Flex vertical style={{ height: '100vh' }}>
      <Typography.Title level={5} style={{ margin: 0, padding: '8px 12px' }}>
        {t('panel.selectionTitle')}
      </Typography.Title>
      <div style={{ flex: 1, minHeight: 0 }}>
        <SelectionPanel scope="popout" />
      </div>
    </Flex>
  );
}

/**
 * The geographic 3D scene in a window of its own — the multi-monitor arrangement the whole
 * two-way sync exists for: the flat map on one screen, the same ground in three dimensions on the
 * other, each following the other's selection and extent over the bus.
 *
 * A pop-out is a separate window with its own copy of every module, so "one scene per window"
 * holds here without anything being arranged: this window's scene is simply not the other's.
 *
 * It writes no URL hash. The window is opened by name and reused, so its address is machinery
 * rather than something anybody shares; the shareable position is the one the 3D route keeps.
 */
function Scene3dScenePanel() {
  const { t } = useTranslation();
  return (
    <Flex vertical style={{ height: '100vh' }}>
      <Typography.Title level={5} style={{ margin: 0, padding: '8px 12px' }}>
        {t('panel.scene3dTitle')}
      </Typography.Title>
      <div style={{ flex: 1, minHeight: 0 }}>
        <Scene3DView />
      </div>
    </Flex>
  );
}

/**
 * Follows cave selections published on the workspace bus (registry pop-out, map click)
 * and renders the selected cave's first 3D survey model — the multi-monitor scenario:
 * map in one window, synced 3D in another.
 */
function Viewer3dPanel() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  // A model named in the address pins this window to it, the way ?document= pins the text
  // pop-out, and for the same reason: the other pop-outs show whatever is selected, while a
  // window opened on one particular model was opened to look at that one and must go on showing
  // it while the reader clicks about elsewhere. Without a model named, this window keeps its old
  // behaviour of following the selection.
  const pinnedModelId = params.get('model');
  const [caveId, setCaveId] = useState<string | null>(null);

  useEffect(() => {
    if (pinnedModelId) {
      return;
    }
    return subscribe((event) => {
      if (
        event.kind === 'selection' &&
        (event.selection?.kind === 'cave' || event.selection?.kind === 'entrance')
      ) {
        setCaveId(event.selection.caveId);
      }
    });
  }, [pinnedModelId]);

  // The pinned model is asked for by its own id; the cave it belongs to comes back with it, so
  // the two paths converge on one cave id and everything below reads the same either way.
  const { data: pinnedModel, isPending: pinnedPending } = useSurveyModel(pinnedModelId ?? undefined);
  const effectiveCaveId = pinnedModelId ? (pinnedModel?.caveId ?? null) : caveId;

  const { data: cave } = useCave(effectiveCaveId ?? undefined);
  const { data: models } = useSurveyModels(pinnedModelId ? undefined : (caveId ?? undefined));
  const { data: entrances } = useEntrances(effectiveCaveId ?? undefined);
  // A cave whose models are all wall meshes has nothing for the survey viewer, and handing it one
  // would produce a parse failure instead of the empty panel that is the truth. Which formats it
  // can read is decided in one place, because this window is not the only thing that asks.
  const followedModel = models?.find(surveyModelReadableByViewer);
  const model = pinnedModelId ? pinnedModel : followedModel;

  // Clicking an entrance label in the 3D scene pans the main window's map there.
  // Survey labels and DB entrance names only sometimes agree, so fall back to the
  // cave's main location when no entrance matches.
  const onEntrancePick = (displayName: string) => {
    const match = entrances?.find((e) => e.name?.toLowerCase() === displayName.toLowerCase());
    if (match) {
      publish({
        kind: 'fly-to',
        lon: Number(match.geom.coordinates[0]),
        lat: Number(match.geom.coordinates[1]),
        zoom: 17,
      });
      publish({ kind: 'selection', selection: { kind: 'entrance', entranceId: match.id, caveId: match.caveId } });
    } else if (cave?.geom) {
      publish({
        kind: 'fly-to',
        lon: Number(cave.geom.coordinates[0]),
        lat: Number(cave.geom.coordinates[1]),
        zoom: 15,
      });
    }
  };

  return (
    <Flex vertical style={{ height: '100vh', padding: 12 }} gap={8}>
      <Typography.Title level={5} style={{ margin: 0 }}>
        {cave ? `${t('panel.viewer3dTitle')} — ${cave.name}` : t('panel.viewer3dTitle')}
      </Typography.Title>
      {model ? (
        <div style={{ flex: 1, minHeight: 0 }}>
          <CaveViewPanel
            fileUrl={model.modelUrl}
            fileName={viewerFileName(model)}
            height="100%"
            onEntrancePick={onEntrancePick}
            // Named so this window can answer a link that points at a station of *this* model,
            // and decline one that points at another cave's.
            surveyModelId={model.id}
          />
        </div>
      ) : (
        <Flex align="center" justify="center" style={{ flex: 1 }}>
          {/* Two different silences. A window following the selection is waiting to be told which
              cave, and "select a cave" is the instruction that ends the wait. A window opened ON a
              model has already been told, so that instruction is not merely unhelpful, it implies
              the reader did something wrong — when what happened is that the model is gone or is
              one whose cave's location is withheld from them, which the server answers as a plain
              404 because a cave's models are its location. */}
          <Typography.Text type="secondary">
            {pinnedModelId && !pinnedPending
              ? t('panel.viewer3dUnavailable')
              : t('panel.viewer3dEmpty')}
          </Typography.Text>
        </Flex>
      )}
    </Flex>
  );
}

function RegistryPanel() {
  const { t } = useTranslation();
  const [params, setParams] = useState<CaveListParams>({ page: 1, pageSize: 20 });
  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput);
  const { data, isFetching } = useCaves({ ...params, search: search || undefined });

  const pick = (cave: CaveListItem) => {
    publish({ kind: 'selection', selection: { kind: 'cave', caveId: cave.id } });
    if (cave.geom) {
      publish({
        kind: 'fly-to',
        lon: cave.geom.coordinates[0],
        lat: cave.geom.coordinates[1],
        zoom: 14,
      });
    }
  };

  return (
    <div style={{ padding: 16, height: '100vh', overflow: 'auto' }}>
      <Flex gap={8} align="center" style={{ marginBottom: 12 }}>
        <Typography.Title level={5} style={{ margin: 0, flex: 1 }}>
          {t('panel.registryTitle')}
        </Typography.Title>
        <Input.Search
          placeholder={t('caves.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 280 }}
          onChange={(e) => setSearchInput(e.target.value)}
        />
      </Flex>
      <Table<CaveListItem>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="small"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={(pagination: TablePaginationConfig) =>
          setParams((p) => ({ ...p, page: pagination.current, pageSize: pagination.pageSize }))}
        onRow={(record) => ({ onClick: () => pick(record), style: { cursor: 'pointer' } })}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: false,
        }}
        columns={[
          { title: t('caves.name'), dataIndex: 'name' },
          { title: t('caves.region'), dataIndex: 'region', width: 140 },
          { title: t('caves.depth'), dataIndex: 'depth', width: 100, align: 'right' },
        ]}
      />
    </div>
  );
}
