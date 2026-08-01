// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import { App, AutoComplete, Input } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useNominatim, useSearch, type FeatureKind } from '../../api/hooks.ts';
import { featureDetailPath } from '../features/featureNavigation.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { flyTo } from '../../map/mapContext.ts';

interface MapSearchProps {
  /** Fill the container rather than the fixed desktop width — the mobile search strip. */
  fullWidth?: boolean;
}

/**
 * Unified search: internal features (every kind) and trip logs, plus Nominatim geocoding.
 * Internal results carry no coordinates — picking one navigates to the record's page
 * (entrances/centerlines resolve to their parent cave); only geocoded places move the map.
 */
export default function MapSearch({ fullWidth = false }: MapSearchProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const [query, setQuery] = useState('');
  const debounced = useDebouncedValue(query);
  const { data: results } = useSearch(debounced);
  const { data: places } = useNominatim(debounced);

  const options = [
    {
      label: t('search.features'),
      options: (results?.features ?? []).map((feature) => ({
        value: `feature:${feature.kind}:${feature.id}`,
        label: `${feature.name ?? t('features.unnamed')} — ${t(`featureKinds.${feature.kind}`)}`,
      })),
    },
    {
      label: t('search.trips'),
      options: (results?.trips ?? []).map((trip) => ({
        value: `trip:${trip.id}`,
        label: `${trip.title} — ${trip.tripDate}`,
      })),
    },
    {
      label: t('map.places'),
      options: (places ?? []).map((place) => ({
        value: `place:${place.place_id}`,
        label: place.display_name,
      })),
    },
  ].filter((group) => group.options.length > 0);

  const onSelect = (value: string) => {
    setQuery('');
    if (value.startsWith('feature:')) {
      const [, kind, id] = value.split(':');
      featureDetailPath(kind as FeatureKind, id)
        .then((path) => navigate(path))
        .catch(() => message.error(t('search.openFailed')));
      return;
    }
    if (value.startsWith('trip:')) {
      navigate(`/trip-logs/${value.slice('trip:'.length)}`);
      return;
    }
    if (value.startsWith('place:')) {
      const place = places?.find((p) => `place:${p.place_id}` === value);
      if (place) {
        flyTo(Number(place.lon), Number(place.lat), 13);
      }
    }
  };

  return (
    <AutoComplete
      value={query}
      options={options}
      style={{ width: fullWidth ? '100%' : 320 }}
      onSearch={setQuery}
      onSelect={onSelect}
      popupMatchSelectWidth={fullWidth ? true : 360}
    >
      <Input prefix={<SearchOutlined />} placeholder={t('map.searchPlaceholder')} allowClear />
    </AutoComplete>
  );
}
