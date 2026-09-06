// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useRef, useState } from 'react';
import { BarChartOutlined, DownloadOutlined, EnvironmentOutlined, PlusOutlined } from '@ant-design/icons';
import { App, Button, Empty, Flex, Input, Table, Tag, Typography } from 'antd';
import type { SorterResult, TablePaginationConfig } from 'antd/es/table/interface';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  useCan,
  useTripLogFacets,
  useTripLogGrouping,
  useTripLogs,
  useTripTypes,
  type TripLogInfo,
} from '../../api/hooks.ts';
import { downloadFile, tripLogExportUrl } from '../../api/download.ts';
import ConfigureLink from '../../components/ConfigureLink.tsx';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import TripStateTag from '../../components/trips/TripStateTag.tsx';
import TripReadinessTag from '../../components/trips/TripReadinessTag.tsx';
import TripFacetPanel from '../../components/trips/TripFacetPanel.tsx';
import TripGroupingPanel from '../../components/trips/TripGroupingPanel.tsx';
import { countPeople } from '../../components/trips/roster.ts';
import { formatTripDates } from '../../components/trips/tripDates.ts';
import { tripTypeLabelOf } from '../../components/trips/tripTypes.ts';
import TripFormModal from './TripFormModal.tsx';
import {
  DefaultTripPageSize,
  NoGrouping,
  clearedTripListFilter,
  isTripListNarrowed,
  readTripListFilter,
  tripFacetQuery,
  tripGroupingQuery,
  tripListQuery,
  tripMapSearch,
  writeTripListFilter,
  type TripListFilter,
} from './tripListFilter.ts';

/** The order the server puts the listing in when nothing is asked for. */
const DefaultSort = '-date';

export default function TripLogListPage() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  // The address is the filter, not a copy of it: a narrowed listing is a link somebody can paste,
  // reload, open in a second tab or walk back to, and all four have to show the same rows.
  const filter = useMemo(() => readTripListFilter(searchParams), [searchParams]);

  /**
   * Every change to the filter goes through here, so there is one place that decides what the
   * back button walks through. A narrowing is a view somebody chose and is worth a history entry;
   * a keystroke on the way to one is not, and neither is turning a page, which says where the
   * reader is standing rather than what they asked to see.
   */
  const apply = (change: Partial<TripListFilter>, replace = false) => {
    const next: TripListFilter = { ...filter, ...change };
    // Any narrowing returns to the first page. Staying on page four of a listing that now has one
    // is an empty table nobody asked for, and it reads as a filter that found nothing.
    if (change.page === undefined) {
      next.page = 1;
    }
    setSearchParams(writeTripListFilter(next), { replace });
  };

  // The box types faster than the address should change, so what is typed is held here and the
  // settled word is written into the address. The ref is what keeps the two honest in both
  // directions: without it, arriving on a link or walking back to one would leave the box showing
  // the word somebody last typed rather than the word the listing is actually narrowed by.
  const [searchInput, setSearchInput] = useState(filter.search);
  const debouncedSearch = useDebouncedValue(searchInput);
  const writtenSearch = useRef(filter.search);
  useEffect(() => {
    if (debouncedSearch === writtenSearch.current) {
      return;
    }
    writtenSearch.current = debouncedSearch;
    apply({ search: debouncedSearch }, true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [debouncedSearch]);
  useEffect(() => {
    if (filter.search !== writtenSearch.current) {
      writtenSearch.current = filter.search;
      setSearchInput(filter.search);
    }
  }, [filter.search]);

  const { data, isFetching, isError } = useTripLogs(tripListQuery(filter));
  // The counts are asked for over the same narrowings the page is, minus the three that change no
  // count — so the number beside an option and the rows that option produces cannot drift apart.
  const { data: facets } = useTripLogFacets(tripFacetQuery(filter));
  const { data: tripTypes } = useTripTypes();
  // Asked for only while a shape is wanted: a listing nobody has grouped fetches no slices.
  const { data: grouping } = useTripLogGrouping(
    tripGroupingQuery(filter),
    filter.groupBy !== NoGrouping,
  );

  // The dashboard's "new trip" action routes here asking for the form to be open on arrival.
  const location = useLocation();
  const [creating, setCreating] = useState(
    Boolean((location.state as { create?: boolean } | null)?.create),
  );
  // Router state is stored in the history entry, so it outlives the modal being closed: without
  // clearing it, going Back to this entry (or reloading it) would re-open the form unasked. The
  // address is carried across unchanged — it holds the filter, and dropping it here would clear
  // whatever narrowing the reader arrived with.
  useEffect(() => {
    if ((location.state as { create?: boolean } | null)?.create) {
      navigate(
        { pathname: location.pathname, search: location.search },
        { replace: true, state: null },
      );
    }
  }, [location.pathname, location.search, location.state, navigate]);

  const canCreate = useCan('tripLogs', 'create');
  // Write, not read: every account may read the vocabularies, so a read check would offer these
  // to everyone. Authoring one decides what every trip under it may say.
  const canWriteTaxonomies = useCan('taxonomies', 'write');

  const sort = filter.sort ?? DefaultSort;
  const orderOf = (key: string) =>
    sort === key ? ('ascend' as const) : sort === `-${key}` ? ('descend' as const) : null;

  /**
   * Paging and ordering are both the server's answer to a new request, never a rearrangement of
   * the rows it already handed back: re-sorting a page would reorder that page, which is a
   * different and wrong answer as soon as there is more than one.
   */
  const onTableChange = (
    pagination: TablePaginationConfig,
    _filters: unknown,
    sorter: SorterResult<TripLogInfo> | SorterResult<TripLogInfo>[],
  ) => {
    const chosen = Array.isArray(sorter) ? sorter[0] : sorter;
    const key = chosen?.columnKey as string | undefined;
    const word = key && chosen?.order ? `${chosen.order === 'descend' ? '-' : ''}${key}` : undefined;
    apply(
      {
        page: pagination.current ?? 1,
        pageSize: pagination.pageSize ?? DefaultTripPageSize,
        sort: word === DefaultSort ? undefined : word,
      },
      true,
    );
  };

  /**
   * The file is the filter and not the page: the server takes the whole narrowed set, so what
   * lands in the spreadsheet is what the count above the table says and not the fifty rows
   * somebody happens to be looking at. It goes through the download helper rather than a plain
   * link because the route needs the caller's token and a link cannot carry one.
   */
  const onExport = () => {
    downloadFile(tripLogExportUrl({ ...tripListQuery(filter) })).catch(() =>
      message.error(t('common.saveFailed')),
    );
  };

  const narrowed = isTripListNarrowed(filter);
  const reset = () => setSearchParams(writeTripListFilter(clearedTripListFilter(filter)));

  const emptyText = isError
    ? t('trips.listFailed')
    : narrowed
      ? t('trips.emptyFiltered')
      : t('trips.empty');

  return (
    <div style={{ padding: 24 }}>
      <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('trips.title')}
        </Typography.Title>
        <Flex gap={8} align="center">
          {/* Offered to everybody, and not gated on anything: the list it opens is worked out
              from whoever is reading it, so it is never a door onto somebody else's trips —
              an account on none of them is shown that, which is a useful answer. */}
          <Button onClick={() => navigate('/trip-logs/mine')}>{t('trips.mine.link')}</Button>
          {/* The narrowing travels; where the reader was standing in the list does not, because
              a map has no page four. */}
          <Button
            icon={<EnvironmentOutlined />}
            data-testid="trip-list-show-on-map"
            onClick={() => navigate({ pathname: '/map', search: tripMapSearch(filter) })}
          >
            {t('trips.filters.showOnMap')}
          </Button>
          {/* The same narrowing, totalled. Where the reader was standing in the list travels
              too, so coming back from the charts returns to the page they left. */}
          <Button
            icon={<BarChartOutlined />}
            data-testid="trip-list-insights"
            onClick={() =>
              navigate({ pathname: '/trip-logs/stats', search: writeTripListFilter(filter).toString() })
            }
          >
            {t('tripStats.open')}
          </Button>
          <Button icon={<DownloadOutlined />} data-testid="trip-list-export" onClick={onExport}>
            {t('common.export')}
          </Button>
          {/* What a purpose asks a report to record, what a roster row may say somebody did, and
              the layout a write-up circulates in — reached from the list they govern, not only
              from the configuration group in the rail. */}
          <ConfigureLink
            items={canWriteTaxonomies
              ? [
                  { key: 'admin/trip-types', label: t('nav.tripTypes') },
                  { key: 'admin/participant-roles', label: t('nav.participantRoles') },
                  { key: 'admin/report-templates', label: t('nav.reportTemplates') },
                ]
              : []}
          />
          {canCreate && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
              {t('trips.new')}
            </Button>
          )}
        </Flex>
      </Flex>
      <Flex gap={8} style={{ marginBottom: 12 }} wrap align="center">
        <Input.Search
          placeholder={t('trips.searchPlaceholder')}
          allowClear
          style={{ maxWidth: 320 }}
          value={searchInput}
          onChange={(e) => setSearchInput(e.target.value)}
        />
        <TripFacetPanel
          filter={filter}
          facets={facets}
          tripTypes={tripTypes}
          onChange={(change) => apply(change)}
          onReset={reset}
        />
      </Flex>
      {/* Permanently above the table, narrowed or not, so what a filter did is never something the
          reader has to work out. Both halves are about this account — how many trips the filter
          leaves, out of how many this account may read at all — so the fraction never implies the
          filter is hiding what access is hiding. */}
      <Flex gap={8} align="center" style={{ marginBottom: 8 }}>
        <Typography.Text type="secondary" data-testid="trip-list-count">
          {t('trips.filters.showing', {
            matching: facets?.matching ?? data?.totalItems ?? 0,
            overall: facets?.overall ?? data?.totalItems ?? 0,
          })}
        </Typography.Text>
        {/* The second reset, where the eye already is. The one in the panel is for somebody who
            went there to change something; this one is for somebody reading the count and
            wondering why it is small. */}
        {narrowed && (
          <Button type="link" size="small" data-testid="trip-list-count-reset" onClick={reset}>
            {t('trips.filters.reset')}
          </Button>
        )}
      </Flex>
      <TripGroupingPanel
        grouping={grouping}
        groupBy={filter.groupBy}
        thenBy={filter.thenBy}
        tripTypes={tripTypes}
        language={i18n.resolvedLanguage}
        onChange={(change) => apply({ ...change, page: filter.page }, true)}
      />
      <Table<TripLogInfo>
        scroll={{ x: 'max-content' }}
        rowKey="id"
        size="middle"
        loading={isFetching && !data}
        dataSource={data?.items}
        onChange={onTableChange}
        locale={{
          emptyText: (
            <Empty
              data-testid="trip-list-empty"
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={emptyText}
            />
          ),
        }}
        onRow={(record) => ({ onClick: () => navigate(`/trip-logs/${record.id}`), style: { cursor: 'pointer' } })}
        pagination={{
          current: data?.page,
          pageSize: data?.pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('trips.date'),
            key: 'date',
            width: 200,
            sorter: true,
            sortOrder: orderOf('date'),
            render: (_, trip) =>
              formatTripDates(trip.tripDate, trip.tripDateEnd, i18n.resolvedLanguage),
          },
          {
            title: t('trips.titleField'),
            key: 'title',
            dataIndex: 'title',
            sorter: true,
            sortOrder: orderOf('title'),
          },
          {
            title: t('trips.state'),
            dataIndex: 'state',
            width: 140,
            render: (value: TripLogInfo['state']) => <TripStateTag state={value} />,
          },
          {
            title: t('trips.type'),
            dataIndex: 'tripTypeId',
            width: 150,
            render: (value: TripLogInfo['tripTypeId']) => {
              const label = tripTypeLabelOf(value, tripTypes, t);
              return label ? <Tag>{label}</Tag> : null;
            },
          },
          {
            title: t('trips.checklist'),
            key: 'readiness',
            width: 140,
            render: (_, trip) => <TripReadinessTag readiness={trip.checklistReadiness} />,
          },
          { title: t('trips.location'), dataIndex: 'locationText', width: 200 },
          {
            title: t('trips.participants'),
            key: 'participants',
            width: 120,
            align: 'right',
            render: (_, trip) => countPeople(trip.participants),
          },
          {
            title: t('features.visibility'),
            dataIndex: 'visibility',
            width: 130,
            render: (value: string) => <Tag>{t(`caves.visibilityValues.${value}`)}</Tag>,
          },
        ]}
      />
      {/* Gated here rather than only on the button: the form also opens from router state, and
          that path must not hand a create form to someone the server would refuse. */}
      {canCreate && (
        <TripFormModal
          open={creating}
          trip={null}
          onClose={(savedId) => {
            setCreating(false);
            if (savedId) {
              navigate(`/trip-logs/${savedId}`);
            }
          }}
        />
      )}
    </div>
  );
}
