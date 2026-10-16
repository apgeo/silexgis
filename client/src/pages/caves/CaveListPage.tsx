// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { DeleteOutlined, DownloadOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Dropdown, Flex, Input, InputNumber, Select, Table, Tag, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { labelledPaginationItems } from '../../components/paginationLabels.ts';
import type { TablePaginationConfig } from 'antd';
import type { SorterResult } from 'antd/es/table/interface';
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
import {
  caveRegistryToken,
  readCaveRegistryAddress,
  writeCaveRegistryAddress,
  type CaveRegistryAddress,
  type CaveRegistryControls,
} from '../../filters/caveRegistry.ts';
import CaveRegistrySavedFilters from './CaveRegistrySavedFilters.tsx';

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

/** A typed number, or nothing when the field is empty or holds no number. */
function lengthOf(raw: string | undefined): number | undefined {
  const text = raw?.trim() ?? '';
  const value = Number(text);
  return text === '' || !Number.isFinite(value) ? undefined : value;
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

  const [karstLinkOpen, setKarstLinkOpen] = useState(false);
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

  /** A new state written to the address. Typing replaces the entry; anything chosen adds one. */
  const go = (update: (current: AddressState) => Partial<AddressState>, replace = false) => {
    const newest = written.current.at(-1);
    const base = newest === undefined ? address : readCaveRegistryAddress(new URLSearchParams(newest));
    const current: AddressState = {
      controls: base.controls,
      extra: base.extra,
      savedFilterId: base.savedFilterId,
      sort: base.sort,
      page: base.page,
      pageSize: base.pageSize,
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

  const [lengthInput, setLengthInput] = useState<number | null>(controls.minLength ?? null);
  useEffect(() => {
    setLengthInput(controls.minLength ?? null);
  }, [controls.minLength]);
  const commitLength = (value: number | undefined) => {
    if (value !== controls.minLength) setControl({ minLength: value });
  };

  const listParams: CaveListParams = {
    page: address.page,
    pageSize: address.pageSize,
    sort: address.sort,
    filter: token,
  };
  const { data, isFetching, error } = useCaves(listParams);

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
    downloadFile(caveExportUrl(format, { filter: token })).catch(() => message.error(t('common.saveFailed')));
  };

  // The same narrowing the list is showing, so the count in the dialog is about the caves on
  // the screen and the file holds those and no others.
  const karstLinkRequest = { filter: token };

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
  ) => {
    const single = Array.isArray(sorter) ? sorter[0] : sorter;
    const field = single?.field ? sortableFields[String(single.field)] : undefined;
    const sort = field && single.order ? `${single.order === 'descend' ? '-' : ''}${field}` : undefined;
    go(() => ({ page: pagination.current ?? 1, pageSize: pagination.pageSize ?? address.pageSize, sort }));
  };

  // The distribution panel decides whether a registry-wide grouping may be laid over these caves
  // from the narrowings it is handed. The plain controls say everything the document does unless
  // there are conditions the controls cannot show — and then it is handed the document, which it
  // knows it cannot honestly group.
  const distributionScope: CaveListParams = extra.length > 0
    ? { filter: token }
    : {
        caveTypeId: controls.caveTypeId,
        region: controls.region,
        search: controls.search,
        minLength: controls.minLength,
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
                  label: t('karstlinkExport.menuItem'),
                  onClick: () => void onKarstLinkExport(),
                },
                ...(karstLinkTreatment === undefined
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
        <InputNumber<number>
          min={0}
          placeholder={t('caveRegistry.minLengthPlaceholder')}
          aria-label={t('caveRegistry.minLengthPlaceholder')}
          style={{ width: 170 }}
          value={lengthInput}
          data-testid="cave-registry-min-length"
          onChange={(value) => setLengthInput(value ?? null)}
          onBlur={(e) => commitLength(lengthOf(e.target.value))}
          onPressEnter={(e) => commitLength(lengthOf((e.target as HTMLInputElement).value))}
        />
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
      </Flex>
      <CaveRegistrySavedFilters
        reading={{ controls, extra }}
        savedFilterId={address.savedFilterId}
        onApply={(reading, savedFilterId) =>
          go(() => ({ controls: reading.controls, extra: reading.extra, savedFilterId, page: 1 }))}
        onSaved={(savedFilterId) => go(() => ({ savedFilterId }))}
        onClearExtra={() => go(() => ({ extra: [], page: 1 }))}
        onForget={() => go(() => ({ savedFilterId: undefined }))}
      />
      {error && token !== undefined && (
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
        onRow={(record) => ({ onClick: () => navigate(`/caves/${record.id}`), style: { cursor: 'pointer' } })}
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
