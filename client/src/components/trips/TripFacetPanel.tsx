// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { ClearOutlined } from '@ant-design/icons';
import { Button, DatePicker, Flex, Select, Space, Typography } from 'antd';
import dayjs from 'dayjs';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import type { TripFacetValue, TripListFacets, TripType } from '../../api/hooks.ts';
import type { TripListFilter } from '../../pages/trips/tripListFilter.ts';
import { tripTypeLabelOf } from './tripTypes.ts';

/**
 * The trip listing's filter panel.
 *
 * Two rules decide how it behaves, and they are the ones people actually predict. Values inside
 * one control are alternatives — any of them keeps a trip — while the controls narrow each other,
 * so adding a choice widens and adding a control narrows. And an empty control is no opinion, not
 * "match nothing": that is what makes clearing one mean the same as never having touched it, and
 * it is what makes a reset definable at all.
 *
 * Every option carries what it would leave, counted for whoever is reading — so somebody with
 * different access sees different numbers for the same option and both are right. The counts come
 * from the server over the same query the page comes from, and each control's own choices are
 * left out of its own numbers, so an option answers "and this one too" rather than "as well as
 * what you already picked here", which would read as zero beside everything unpicked.
 */
export interface TripFacetPanelProps {
  filter: TripListFilter;
  facets: TripListFacets | undefined;
  tripTypes: TripType[] | undefined;
  onChange: (change: Partial<TripListFilter>) => void;
  onReset: () => void;
}

/** An option as antd wants it: one searchable string carrying the name and what it would leave. */
interface FacetOption {
  value: string;
  label: string;
}

const countedLabel = (name: string, count: number) => `${name} (${count})`;

/**
 * The options to offer for one control.
 *
 * The server only counts what the rest of the filter still reaches, so an option that would leave
 * nothing is simply not offered — which is the whole point of counting them. That would also drop
 * a choice somebody has already made the moment their other choices exclude it, leaving a control
 * displaying a raw identifier and no way to let go of it. So a chosen value absent from the answer
 * is kept, and shown honestly as leaving none.
 */
function optionsFor(
  values: TripFacetValue[] | undefined,
  chosen: string[],
  name: (value: string) => string,
): FacetOption[] {
  const offered = values ?? [];
  const known = new Set(offered.map((option) => option.value));
  return [
    ...offered.map((option) => ({
      value: option.value,
      label: countedLabel(option.label ?? name(option.value), option.count),
    })),
    ...chosen
      .filter((value) => !known.has(value))
      .map((value) => ({ value, label: countedLabel(name(value), 0) })),
  ];
}

const incidentName = (value: string, t: TFunction) =>
  value === 'true' ? t('trips.filters.incidentYes') : t('trips.filters.incidentNo');

export default function TripFacetPanel({
  filter,
  facets,
  tripTypes,
  onChange,
  onReset,
}: TripFacetPanelProps) {
  const { t } = useTranslation();

  const typeOptions = useMemo(
    () =>
      optionsFor(
        facets?.types,
        filter.types,
        (value) => tripTypeLabelOf(Number(value), tripTypes, t) ?? value,
      ),
    [facets?.types, filter.types, tripTypes, t],
  );
  const stateOptions = useMemo(
    () => optionsFor(facets?.states, filter.states, (value) => t(`trips.stateValues.${value}`)),
    [facets?.states, filter.states, t],
  );
  const visibilityOptions = useMemo(
    () =>
      optionsFor(facets?.visibilities, filter.visibilities, (value) =>
        t(`caves.visibilityValues.${value}`),
      ),
    [facets?.visibilities, filter.visibilities, t],
  );
  const incidentOptions = useMemo(
    () =>
      optionsFor(
        facets?.incident,
        filter.hadIncident === undefined ? [] : [String(filter.hadIncident)],
        (value) => incidentName(value, t),
      ),
    [facets?.incident, filter.hadIncident, t],
  );
  // The two open-ended controls name rows rather than words, so the server sends the names with
  // the counts — a roster and an area register are not vocabularies this client holds. It also
  // sends back whatever is currently chosen whether or not the count list reaches that far, which
  // is what keeps a shared link naming somebody far down the roster from arriving as a raw
  // identifier here.
  const participantOptions = useMemo(
    () => optionsFor(facets?.participants, filter.participantIds, (value) => value),
    [facets?.participants, filter.participantIds],
  );
  const areaOptions = useMemo(
    () => optionsFor(facets?.areas, filter.areaIds, (value) => value),
    [facets?.areas, filter.areaIds],
  );

  // Every control is searchable and shows its chosen values as tags inside its own label, so a
  // narrowed panel says what it is narrowed by without being opened.
  const many = {
    mode: 'multiple' as const,
    allowClear: true,
    showSearch: true,
    optionFilterProp: 'label',
    maxTagCount: 'responsive' as const,
    style: { minWidth: 200, maxWidth: 320 },
  };
  const single = {
    allowClear: true,
    showSearch: true,
    optionFilterProp: 'label',
    style: { minWidth: 200, maxWidth: 320 },
  };

  return (
    <Flex wrap gap={8} align="center" data-testid="trip-facets">
      <Select
        {...many}
        data-testid="trip-facet-types"
        placeholder={t('trips.type')}
        value={filter.types}
        options={typeOptions}
        onChange={(values: string[]) => onChange({ types: values })}
      />
      <Select
        {...many}
        data-testid="trip-facet-states"
        placeholder={t('trips.state')}
        value={filter.states}
        options={stateOptions}
        onChange={(values: string[]) => onChange({ states: values })}
      />
      <Select
        {...many}
        data-testid="trip-facet-visibilities"
        placeholder={t('features.visibility')}
        value={filter.visibilities}
        options={visibilityOptions}
        onChange={(values: string[]) => onChange({ visibilities: values })}
      />
      <Select
        {...single}
        data-testid="trip-facet-incident"
        placeholder={t('trips.filters.incident')}
        value={filter.hadIncident === undefined ? undefined : String(filter.hadIncident)}
        options={incidentOptions}
        onChange={(value?: string) =>
          onChange({ hadIncident: value === undefined ? undefined : value === 'true' })
        }
      />
      <Select
        {...many}
        data-testid="trip-facet-participant"
        placeholder={t('trips.filters.participant')}
        value={filter.participantIds}
        options={participantOptions}
        onChange={(values: string[]) => onChange({ participantIds: values })}
      />
      <Select
        {...many}
        data-testid="trip-facet-area"
        placeholder={t('trips.filters.area')}
        value={filter.areaIds}
        options={areaOptions}
        onChange={(values: string[]) => onChange({ areaIds: values })}
      />
      {/* The window is an overlap, not a start date: a trip running across the end of a month is
          found by that month and by the next one. Either bound stands alone, and both clear. */}
      <DatePicker.RangePicker
        data-testid="trip-facet-dates"
        allowEmpty={[true, true]}
        // Its own wording rather than the picker's default, because the trip form on this same
        // page asks for a trip's own start and end: two controls reading "Start date" on one
        // screen mean two different things and neither says which.
        placeholder={[t('trips.filters.fromDate'), t('trips.filters.toDate')]}
        value={[
          filter.from ? dayjs(filter.from) : null,
          filter.to ? dayjs(filter.to) : null,
        ]}
        onChange={(range) =>
          onChange({
            from: range?.[0]?.format('YYYY-MM-DD'),
            to: range?.[1]?.format('YYYY-MM-DD'),
          })
        }
      />
      <Space size={4}>
        <Button
          data-testid="trip-facets-reset"
          icon={<ClearOutlined />}
          onClick={onReset}
        >
          {t('trips.filters.reset')}
        </Button>
        {/* Said once, here, because the counts beside the options are counts of what this reader
            may open — two accounts see different numbers for the same option and neither is
            wrong. Without it the panel looks like it is disagreeing with itself. */}
        <Typography.Text type="secondary">{t('trips.filters.yourAccess')}</Typography.Text>
      </Space>
    </Flex>
  );
}
