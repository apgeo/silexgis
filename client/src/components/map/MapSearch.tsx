// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import { AutoComplete, Input } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCaveSearch, useNominatim } from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { flyTo } from '../../map/mapContext.ts';

interface SearchTarget {
  lon: number;
  lat: number;
  zoom: number;
}

/** Unified search over the workspace map: internal caves + Nominatim geocoding. */
export default function MapSearch() {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const debounced = useDebouncedValue(query);
  const { data: caveResults } = useCaveSearch(debounced);
  const { data: places } = useNominatim(debounced);

  const targets = useMemo(() => {
    const map = new Map<string, SearchTarget>();
    for (const cave of caveResults?.caves ?? []) {
      if (cave.mainGeom) {
        map.set(`cave:${cave.id}`, {
          lon: cave.mainGeom.coordinates[0],
          lat: cave.mainGeom.coordinates[1],
          zoom: 15,
        });
      }
    }
    for (const place of places ?? []) {
      map.set(`place:${place.place_id}`, {
        lon: Number(place.lon),
        lat: Number(place.lat),
        zoom: 13,
      });
    }
    return map;
  }, [caveResults, places]);

  const options = [
    {
      label: t('map.caves'),
      options: (caveResults?.caves ?? []).map((cave) => ({
        value: `cave:${cave.id}`,
        label: `${cave.name}${cave.region ? ` — ${cave.region}` : ''}`,
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

  return (
    <AutoComplete
      value={query}
      options={options}
      style={{ width: 320 }}
      onSearch={setQuery}
      onSelect={(value: string) => {
        const target = targets.get(value);
        if (target) {
          flyTo(target.lon, target.lat, target.zoom);
        }
        setQuery('');
      }}
      popupMatchSelectWidth={360}
    >
      <Input prefix={<SearchOutlined />} placeholder={t('map.searchPlaceholder')} allowClear />
    </AutoComplete>
  );
}
