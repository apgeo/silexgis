// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { AimOutlined, DeleteOutlined, DownloadOutlined, FilterOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Drawer, Dropdown, Flex, Input, Select, Table, Tag, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { labelledPaginationItems } from '../../components/paginationLabels.ts';
import type { TablePaginationConfig } from 'antd';
import type { SorterResult, TableCurrentDataSource } from 'antd/es/table/interface';
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { caveExportUrl, DownloadError, downloadFile } from '../../api/download.ts';
import {
  useCan,
  useCaveTypes,
  useCaves,
  useCreateDoor,
  useTags,
  type CaveListItem,
  type CaveListParams,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import CaveDistributionPanel from '../../components/statistics/CaveDistributionPanel.tsx';
import KarstLinkExportModal from '../../components/caves/KarstLinkExportModal.tsx';
import { exportKarstLinkWithStoredAnswer } from '../../components/caves/karstLinkExport.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import FilterEditor from '../../components/filters/FilterEditor.tsx';
import {
  CAVE_REGISTRY_SCOPE,
  CAVE_WORLD,
  caveRegistryEditorQuestion,
  caveRegistryListToken,
  caveRegistryOrderAfterEdit,
  caveRegistryToken,
  readCaveRegistryAddress,
  readCaveRegistryEditor,
  writeCaveRegistryAddress,
  type CaveRegistryAddress,
  type CaveRegistryControls,
  type LengthComparison,
} from '../../filters/caveRegistry.ts';
import { describeComparison, formatSigned, readComparison, type Reading } from '../../filters/typedComparison.ts';
import CaveRegistrySavedFilters from './CaveRegistrySavedFilters.tsx';
import SelectResultButton from '../../components/filters/SelectResultButton.tsx';
import { mapAddressForList } from '../../filters/tableMapHandoff.ts';
import { useTableMapLink } from '../../workspace/tableMapLink.ts';

const exportFormats = ['csv', 'geojson', 'gpx', 'kml', 'shapefile'] as const;

/**
 * The interchange format sits beside the vector ones but is not one of them: it is the only
 * export that has a question to ask before it runs, so unless the answer is already settled it
 * opens a dialog rather than starting a download.
 */
const karstLinkKey = 'karstlink';

/** Reaching the question again once it has been answered once. */
const karstLinkChangeKey = 'karstlink-change';

const sortableFields: Record<string, string> = {
  name: 'name',
  region: 'region',
  surveyedLength: 'surveyedLength',
  depth: 'depth',
  updatedAt: 'updatedAt',
};

type AddressState = Omit<CaveRegistryAddress, 'unreadableLink'>;

/**
 * The orders the advanced filter offers here: nearest first, which only it can choose, and the
 * registry's own order by last change to go back to. The columns keep the rest.
 */
const EDITOR_SORTS = ['updated', 'proximity'] as const;

/** What the length box holds, read as it means a bare number: at least that long. */
function readLength(text: string): Reading {
  return readComparison(text, 'number', 'greaterOrEqual');
}

function lengthText(length: LengthComparison | undefined): string {
  return length === undefined ? '' : formatSigned(length, 'greaterOrEqual');
}

function sameLength(a: LengthComparison | undefined, b: LengthComparison | undefined): boolean {
  return JSON.stringify(a ?? null) === JSON.stringify(b ?? null);
}

/**
 * The cave registry.
 *
 * Its whole state lives in the address: the controls compose one filter document, carried as a
 * token, beside the table's sort and page. So a pasted link, a reload, the back button and a saved
 * filter all reproduce the same list by the same road, and the exports are asked with the very
 * token the list was — the file holds the caves on the screen.
 */
export default function CaveListPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [searchParams, setSearchParams] = useSearchParams();
  const address = useMemo(() => readCaveRegistryAddress(searchParams), [searchParams]);
  const { controls, extra } = address;
  const token = caveRegistryToken(address);
  // The list alone is asked with the place nearest first is measured from; the exports and a
  // saved filter keep the plain question.
  const listToken = caveRegistryListToken(address);

  const [karstLinkOpen, setKarstLinkOpen] = useState(false);

  // The advanced filter edits the same question the controls do, read from the address when it
  // opens and written back to it when applied, so a link, a reload and a saved filter carry what
  // was written there exactly as they carry a control. Whatever opened it gets the focus back.
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const advancedOpener = useRef<HTMLElement | null>(null);
  const advancedButton = useRef<HTMLButtonElement | null>(null);
  const openAdvanced = (opener: HTMLElement | null) => {
    advancedOpener.current = opener;
    setAdvancedOpen(true);
  };
  // Focus is given back a frame after the close is asked for, and again a frame after the drawer has
  // finished closing, so that the drawer's own handling of focus on close — which may put it back on
  // whatever it found focused — does not have the last word. The first is not optional: the drawer
  // does not always play its closing transition (it was seen skipping it when reopened after a
  // reload), and then it never reports that it has closed while its focused button is simply removed,
  // leaving the focus on the page itself.
  const returnFocus = () => {
    requestAnimationFrame(() => {
      const opener = advancedOpener.current;
      (opener?.isConnected ? opener : advancedButton.current)?.focus();
    });
  };
  const closeAdvanced = () => {
    setAdvancedOpen(false);
    returnFocus();
  };
  const karstLinkTreatment = useUiPrefsStore((s) => s.karstLinkTreatment);
  const { data: caveTypes } = useCaveTypes();
  const { data: tags } = useTags('');

  // What this page has written to its address and not yet seen arrive, newest last. The router applies a write a moment
  // after it is asked for, so two writes close together — a field committed while the search box's
  // pause runs out — would otherwise each start from the address as it was before either, and the
  // second would quietly undo the first. A write starts from the newest of these instead, until the
  // address arrives at something this page did not write (back, forward, a pasted link).
  const written = useRef<string[]>([]);
  /** Whether the address last arrived at was one this page wrote itself. */
  const arrivedByOwnWrite = useRef(false);
  useEffect(() => {
    // Arriving at one of these settles it and everything older; arriving anywhere else — which is
    // also where going back from a settled write leads — forgets them all.
    const arrived = written.current.indexOf(searchParams.toString());
    written.current = arrived === -1 ? [] : written.current.slice(arrived);
    arrivedByOwnWrite.current = arrived !== -1;
  }, [searchParams]);

  /** The address as this page last wrote it, or as it arrived when no write of its own is on its way. */
  const newestAddress = () => {
    const newest = written.current.at(-1);
    return newest === undefined ? address : readCaveRegistryAddress(new URLSearchParams(newest));
  };

  // The advanced filter opens on the newest question too. The address bar changes at once but the
  // page learns of it a moment later — longer on a busy machine — so a search that has just
  // reached the address would otherwise be missing from the editor opened in that moment, and when
  // it did arrive the editor would be seeded again over whatever had been written in it meanwhile.
  const editorAddress = newestAddress();
  const editorDocument = caveRegistryEditorQuestion(editorAddress);

  /** A new state written to the address. Typing replaces the entry; anything chosen adds one. */
  const go = (update: (current: AddressState) => Partial<AddressState>, replace = false) => {
    const base = newestAddress();
    const current: AddressState = {
      controls: base.controls,
      extra: base.extra,
      savedFilterId: base.savedFilterId,
      sort: base.sort,
      near: base.near,
      page: base.page,
      pageSize: base.pageSize,
      bbox: base.bbox,
    };
    const params = writeCaveRegistryAddress({ ...current, ...update(current) });
    written.current = [...written.current, params.toString()];
    setSearchParams(params, { replace });
  };

  /** One control changed: back to the first page, the saved filter kept so the change shows. */
  const setControl = (patch: CaveRegistryControls, replace = false) =>
    go((current) => ({ controls: { ...current.controls, ...patch }, page: 1 }), replace);

  // The search box is typed into, so it holds its own text and reaches the address only once the
  // typing pauses. The address is still the truth: when it changes under the box — back, forward,
  // a saved filter applied — the box follows it.
  const [searchInput, setSearchInput] = useState(controls.search ?? '');
  const search = useDebouncedValue(searchInput);
  const latest = useRef({ controls, setControl });
  useEffect(() => {
    latest.current = { controls, setControl };
  });
  useEffect(() => {
    const arrived = controls.search ?? '';
    // The box's own text comes back from the address trimmed; putting that over the box would take
    // away the space somebody has just typed before their next word. So the address wins only when
    // it says something else, or when it arrived by a road this page did not take itself.
    const ownWrite = arrivedByOwnWrite.current;
    setSearchInput((typed) => (ownWrite && typed.trim() === arrived.trim() ? typed : arrived));
  }, [controls.search]);
  useEffect(() => {
    const { controls: now, setControl: write } = latest.current;
    if ((search.trim() || undefined) !== (now.search?.trim() || undefined)) {
      write({ search: search.trim() || undefined }, true);
    }
    // Only a settled change of what was typed writes; reacting to the address as well would type
    // the old text back over a step somebody just went back from.
  }, [search]);

  const [regionInput, setRegionInput] = useState(controls.region ?? '');
  useEffect(() => {
    setRegionInput(controls.region ?? '');
  }, [controls.region]);
  const commitRegion = (text: string) => {
    const value = text.trim() || undefined;
    if (value !== controls.region) setControl({ region: value });
  };

  const committedLength = lengthText(controls.length);
  const [lengthInput, setLengthInput] = useState(committedLength);
  useEffect(() => {
    setLengthInput(committedLength);
  }, [committedLength]);
  const lengthReading = readLength(lengthInput);
  // Text that cannot be read changes nothing: the list keeps the length it had, and the box says why.
  const commitLength = (text: string) => {
    const reading = readLength(text);
    if (reading.kind === 'unreadable') return;
    const value: LengthComparison | undefined = reading.kind === 'empty'
      ? undefined
      : { op: reading.comparison.op, values: reading.comparison.values as number[], negated: reading.comparison.negated };
    if (!sameLength(value, controls.length)) setControl({ length: value });
  };

  const listParams: CaveListParams = {
    page: address.page,
    pageSize: address.pageSize,
    sort: address.sort,
    filter: listToken,
    // The map's window, when the list was opened from it: the server decides which caves are in
    // it, by the point each reader is shown, and counts the page over the same set.
    bbox: address.bbox,
  };
  const { data, isFetching, error } = useCaves(listParams);
  const tableMap = useTableMapLink<CaveListItem>();

  const canCreate = useCan('features', 'create');
  // Whoever could have deleted a cave is shown where one is put back from. An owner deletes
  // their own cave by owning it, which no capability spells as "delete" — so being able to
  // create one, which is what makes an owner, opens the door too; and that is asked of the
  // create door rather than of the right over caves as such, because somebody who records
  // caves only for their caving group owns those caves like anybody else. The page behind the
  // door is answered row by row from what its reader may restore; this only decides whether
  // the door is worth showing.
  const canDelete = useCan('features', 'delete');
  const { canCreate: mayOwn } = useCreateDoor('features');
  const canRestore = mayOwn || canDelete;
  const typeName = (id: number) => caveTypes?.find((x) => x.id === id)?.name ?? '';

  // Exports carry the list's own filter (not the current page — the server streams all rows).
  const onExport = (format: string) => {
    downloadFile(caveExportUrl(format, { filter: token, bbox: address.bbox })).catch(() => message.error(t('common.saveFailed')));
  };

  // The same filter the list is showing, so the count in the dialog is about the caves on the
  // screen and the file holds those and no others. The map's window is deliberately not sent:
  // the interchange export keeps every protected cave its reader may not place inside any window
  // (so that its membership cannot be walked onto a hidden position), while the list keeps one
  // only when its published point is inside. Under a window the two would hold different caves,
  // so the action is withheld while the list is cut to the map's window rather than producing a
  // file that claims to be the rows on the screen and is not.
  const karstLinkRequest = { filter: token };
  const karstLinkWithheld = address.bbox !== undefined;

  /**
   * The interchange export, asked for from the menu.
   *
   * Somebody who ticked "stop asking me" is not asked: their answer travels with the request
   * and the file starts downloading. That is what the checkbox promised, and a dialog that
   * reappeared with the answer pre-selected would be collecting a confirmation the person had
   * already given.
   *
   * Any refusal puts the dialog back up rather than only showing an error. The two refusals
   * this path can actually meet — a set too large for one file, and a stored answer this
   * server no longer understands — are both things the dialog states properly and both are
   * recovered from by choosing again.
   */
  const onKarstLinkExport = async () => {
    if (karstLinkTreatment === undefined) {
      setKarstLinkOpen(true);
      return;
    }

    try {
      await exportKarstLinkWithStoredAnswer(karstLinkRequest, karstLinkTreatment);
    } catch (failure) {
      const code = failure instanceof DownloadError ? failure.code : undefined;
      message.error(code === undefined ? t('common.saveFailed') : t('karstlinkExport.askAgain'));
      setKarstLinkOpen(true);
    }
  };

  const onTableChange = (
    pagination: TablePaginationConfig,
    _filters: unknown,
    sorter: SorterResult<CaveListItem> | SorterResult<CaveListItem>[],
    change: TableCurrentDataSource<CaveListItem>,
  ) => {
    const paging = { page: pagination.current ?? 1, pageSize: pagination.pageSize ?? address.pageSize };
    // Turning a page keeps the order, nearest first included, which no column shows as its own.
    if (change.action !== 'sort') {
      go(() => paging);
      return;
    }
    const single = Array.isArray(sorter) ? sorter[0] : sorter;
    const field = single?.field ? sortableFields[String(single.field)] : undefined;
    const sort = field && single.order ? `${single.order === 'descend' ? '-' : ''}${field}` : undefined;
    go(() => ({ ...paging, sort }));
  };

  // The distribution panel decides whether a registry-wide grouping may be laid over these caves
  // from the narrowings it is handed. The plain controls say everything the document does unless
  // there are conditions the controls cannot show — and then it is handed the document, which it
  // knows it cannot honestly group.
  const plainLength = controls.length === undefined
    || (controls.length.op === 'greaterOrEqual' && !controls.length.negated);
  const distributionScope: CaveListParams = extra.length > 0 || !plainLength
    ? { filter: token, bbox: address.bbox }
    : {
        bbox: address.bbox,
        caveTypeId: controls.caveTypeId,
        region: controls.region,
        search: controls.search,
        minLength: controls.length?.values[0],
        tag: controls.tag,
      };

  const sortOrder = (field: string) =>
    address.sort === field ? ('ascend' as const) : address.sort === `-${field}` ? ('descend' as const) : null;

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('caves.title')}
        </Typography.Title>
        <Flex gap={8}>
          {/* The map is opened with this list's own document as its filter, so it draws the
              caves listed here — the same question, run by the map's routes. */}
          <Button
            icon={<AimOutlined />}
            data-testid="cave-registry-show-on-map"
            onClick={() => navigate(mapAddressForList(token))}
          >
            {t('tableMap.showOnMap')}
          </Button>
          <SelectResultButton
            list="caves"
            // The list's own request, nearest first's place included, so the set holds the
            // first rows this list shows in the order it shows them.
            query={{ filter: listToken, sort: address.sort, bbox: address.bbox }}
            total={data?.totalItems}
            data-testid="cave-registry-select-all"
          />
          <Dropdown
            menu={{
              items: [
                ...exportFormats.map((format) => ({
                  key: format,
                  label: format.toUpperCase(),
                  onClick: () => onExport(format),
                })),
                { type: 'divider' as const, key: 'karstlink-divider' },
                {
                  key: karstLinkKey,
                  disabled: karstLinkWithheld,
                  label: karstLinkWithheld ? (
                    <Flex vertical data-testid="cave-registry-karstlink-withheld">
                      <span>{t('karstlinkExport.menuItem')}</span>
                      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                        {t('karstlinkExport.notInWindow')}
                      </Typography.Text>
                    </Flex>
                  ) : (
                    t('karstlinkExport.menuItem')
                  ),
                  onClick: () => void onKarstLinkExport(),
                },
                ...(karstLinkTreatment === undefined || karstLinkWithheld
                  ? []
                  : [
                      {
                        key: karstLinkChangeKey,
                        label: t('karstlinkExport.changeAnswer'),
                        onClick: () => setKarstLinkOpen(true),
                      },
                    ]),
              ],
            }}
          >
            <Button icon={<DownloadOutlined />}>{t('common.export')}</Button>
          </Dropdown>
          {canRestore && (
            <Button
              icon={<DeleteOutlined />}
              data-testid="cave-list-deleted"
              onClick={() => navigate('/features/deleted')}
            >
              {t('features.deleted.link')}
            </Button>
          )}
          {canCreate && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/caves/new')}>
              {t('caves.newCave')}
            </Button>
          )}
        </Flex>
      </Flex>
      {address.unreadableLink && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="cave-registry-unreadable-link"
          title={t('caveRegistry.unreadableLink')}
        />
      )}
      {address.unreadableWindow && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="cave-registry-unreadable-window"
          title={t('tableMap.unreadableWindow')}
        />
      )}
      {address.bbox && (
        <Flex gap={4} align="center" style={{ marginBottom: 12 }} data-testid="cave-registry-window">
          <Tag color="blue">{t('tableMap.inMapWindow')}</Tag>
          {/* A button rather than the tag's own cross, so the keyboard reaches it too. */}
          <Button
            size="small"
            type="link"
            data-testid="cave-registry-clear-window"
            onClick={() => go(() => ({ bbox: undefined, page: 1 }))}
          >
            {t('tableMap.clearWindow')}
          </Button>
        </Flex>
      )}
      <Flex gap={8} wrap style={{ marginBottom: 12 }}>
        <Input.Search
          placeholder={t('caves.searchPlaceholder')}
          aria-label={t('caves.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          value={searchInput}
          data-testid="cave-registry-search"
          onChange={(e) => setSearchInput(e.target.value)}
        />
        <Select
          allowClear
          placeholder={t('caves.allTypes')}
          aria-label={t('caves.allTypes')}
          style={{ width: 200 }}
          value={controls.caveTypeId}
          data-testid="cave-registry-type"
          options={caveTypes?.map((x) => ({ value: Number(x.id), label: x.name }))}
          onChange={(value?: number) => setControl({ caveTypeId: value })}
        />
        {/* Region and length are typed into, so each holds its own text and reaches the address
            when the field is left or Enter is pressed; the address still wins when it changes. */}
        <Input
          allowClear
          placeholder={t('caveRegistry.regionPlaceholder')}
          aria-label={t('caveRegistry.regionPlaceholder')}
          style={{ width: 180 }}
          value={regionInput}
          data-testid="cave-registry-region"
          onChange={(e) => {
            setRegionInput(e.target.value);
            // The clear button empties the field without leaving it, so that is taken at once.
            if (e.type !== 'change') commitRegion('');
          }}
          onBlur={() => commitRegion(regionInput)}
          onPressEnter={() => commitRegion(regionInput)}
        />
        <Input
          allowClear
          placeholder={t('caveRegistry.lengthPlaceholder')}
          aria-label={t('caveRegistry.lengthPlaceholder')}
          status={lengthReading.kind === 'unreadable' ? 'error' : undefined}
          style={{ width: 200 }}
          value={lengthInput}
          data-testid="cave-registry-min-length"
          onChange={(e) => {
            setLengthInput(e.target.value);
            if (e.type !== 'change') commitLength('');
          }}
          onBlur={() => commitLength(lengthInput)}
          onPressEnter={() => commitLength(lengthInput)}
        />
        {lengthReading.kind === 'unreadable' && (
          <Typography.Text type="danger" role="alert" data-testid="cave-registry-length-unreadable">
            {t('filters.typed.unreadable')}
          </Typography.Text>
        )}
        {lengthReading.kind === 'read' && (
          <Typography.Text type="secondary" aria-live="polite" data-testid="cave-registry-length-understood">
            {(() => {
              const described = describeComparison({ ...lengthReading.comparison, unit: 'm' }, 'number');
              return t('filters.typed.understood', { text: t(described.key, described.values) });
            })()}
          </Typography.Text>
        )}
        <Select
          allowClear
          showSearch
          optionFilterProp="label"
          placeholder={t('tags.filterPlaceholder')}
          aria-label={t('tags.filterPlaceholder')}
          style={{ width: 200 }}
          value={controls.tag}
          data-testid="cave-registry-tag"
          options={tags?.map((x) => ({ value: x.slug, label: x.name }))}
          onChange={(value?: string) => setControl({ tag: value })}
        />
        <Button
          ref={advancedButton}
          icon={<FilterOutlined />}
          onClick={(e) => openAdvanced(e.currentTarget)}
          aria-haspopup="dialog"
          data-testid="cave-registry-advanced"
        >
          {t('caveRegistry.advanced')}
        </Button>
      </Flex>
      <Drawer
        open={advancedOpen}
        title={t('caveRegistry.advancedTitle')}
        size="large"
        destroyOnHidden
        onClose={closeAdvanced}
        afterOpenChange={(open) => {
          if (!open) returnFocus();
        }}
      >
        {/* Keyed by the question it opens on, so an address that moves while it is open (back,
            forward, a write that arrived late) re-seeds it rather than leaving a draft of an older
            question to be applied over the newer one. */}
        <FilterEditor
          key={JSON.stringify(editorDocument)}
          world={CAVE_WORLD}
          document={editorDocument}
          fixed={CAVE_REGISTRY_SCOPE}
          sortable
          sorts={EDITOR_SORTS}
          onCancel={closeAdvanced}
          onApply={(edited) => {
            const reading = readCaveRegistryEditor(edited);
            const order = caveRegistryOrderAfterEdit(edited, editorDocument, editorAddress.sort);
            go(() => ({ controls: reading.controls, extra: reading.extra, ...order, page: 1 }));
            closeAdvanced();
          }}
        />
      </Drawer>
      <CaveRegistrySavedFilters
        reading={{ controls, extra }}
        savedFilterId={address.savedFilterId}
        onApply={(reading, savedFilterId) =>
          go(() => ({ controls: reading.controls, extra: reading.extra, savedFilterId, page: 1 }))}
        onSaved={(savedFilterId) => go(() => ({ savedFilterId }))}
        onClearExtra={() => go(() => ({ extra: [], page: 1 }))}
        onEditExtra={(opener) => openAdvanced(opener)}
        onForget={() => go(() => ({ savedFilterId: undefined }))}
        onRelease={() => go(() => ({ controls: {}, extra: [], savedFilterId: undefined, page: 1 }))}
      />
      {error && listToken !== undefined && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="cave-registry-filter-refused"
          title={t('caveRegistry.filterRefused')}
        />
      )}
      <Table<CaveListItem>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching}
        dataSource={data?.items}
        onChange={onTableChange}
        rowClassName={tableMap.rowClassName}
        onRow={(record) => ({
          onClick: () => navigate(`/caves/${record.id}`),
          style: { cursor: 'pointer' },
          ...tableMap.rowHover(record),
        })}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
          itemRender: labelledPaginationItems(t),
        }}
        columns={[
          { title: t('caves.name'), dataIndex: 'name', sorter: true, sortOrder: sortOrder('name') },
          { title: t('caves.identificationCode'), dataIndex: 'identificationCode', width: 120 },
          { title: t('caves.type'), dataIndex: 'caveTypeId', width: 160, render: typeName },
          { title: t('caves.region'), dataIndex: 'region', sorter: true, width: 140, sortOrder: sortOrder('region') },
          {
            title: t('caves.surveyedLength'),
            dataIndex: 'surveyedLength',
            sorter: true,
            width: 120,
            align: 'right',
            sortOrder: sortOrder('surveyedLength'),
          },
          {
            title: t('caves.depth'),
            dataIndex: 'depth',
            sorter: true,
            width: 110,
            align: 'right',
            sortOrder: sortOrder('depth'),
          },
          { title: t('caves.entrances'), dataIndex: 'entranceCount', width: 100, align: 'right' },
          {
            title: t('caves.visibility'),
            dataIndex: 'visibility',
            width: 130,
            render: (value: string) => <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>,
          },
        ]}
      />
      {/* The panel is handed the same narrowing the list ran, so it can tell whether a
          registry-wide grouping may honestly be laid over these caves. */}
      <CaveDistributionPanel
        caves={data?.items ?? []}
        typeName={typeName}
        scope={distributionScope}
      />
      <KarstLinkExportModal
        open={karstLinkOpen}
        onClose={() => setKarstLinkOpen(false)}
        request={karstLinkRequest}
      />
    </div>
  );
}
