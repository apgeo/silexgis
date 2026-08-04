// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, type ReactNode } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import { App, AutoComplete, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useNominatim, useSearch, type FeatureKind } from '../../api/hooks.ts';
import { downloadFile } from '../../api/download.ts';
import { featureDetailPath } from '../features/featureNavigation.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import { flyTo } from '../../map/mapContext.ts';
import SearchDocumentHit from './SearchDocumentHit.tsx';

interface MapSearchProps {
  /** Fill the container rather than the fixed desktop width — the mobile search strip. */
  fullWidth?: boolean;
}

/**
 * Unified search: internal features (every kind), trip logs and document text, plus Nominatim
 * geocoding. Internal results carry no coordinates — picking one navigates to the record's page
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

  // Documents are matched by what they say, so the group carries a snippet rather than a name,
  // and it is the one group with a total: the server pages it, and the number that comes back
  // was counted by the same query that chose the rows, under the same rules. Nothing here is
  // filtered a second time — a count that disagreed with the list beside it would announce
  // exactly what the list had declined to show.
  const documents = results?.documents;
  const documentOptions: { value: string; label: ReactNode; disabled?: boolean }[] = (
    documents?.items ?? []
  ).map((hit) => ({
    value: `document:${hit.fileId}`,
    label: <SearchDocumentHit hit={hit} />,
  }));

  const foundNothing =
    results !== undefined &&
    results.features.length === 0 &&
    results.trips.length === 0 &&
    results.documents.totalItems === 0;

  if (foundNothing) {
    // Said only when the whole search came back empty, because that is the moment the question
    // is actually asked. A scanned page holds no text to search until somebody types it up —
    // a real answer about the file rather than a gap in the index — and left unsaid it would
    // let an empty result read as "this archive has nothing about that". Said on every search
    // that merely happens to match no prose it would be noise, and noise is not honesty.
    documentOptions.push({
      value: 'documents:no-text',
      disabled: true,
      label: (
        <Typography.Text type="secondary" style={{ fontSize: 12, whiteSpace: 'normal' }}>
          {t('search.noTextLayerHint')}
        </Typography.Text>
      ),
    });
  } else if (documents && documents.totalItems > documents.items.length) {
    documentOptions.push({
      value: 'documents:more',
      disabled: true,
      label: (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('search.moreDocuments', {
            shown: documents.items.length,
            total: documents.totalItems,
          })}
        </Typography.Text>
      ),
    });
  }

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
      label: t('search.documents'),
      options: documentOptions,
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
    if (value.startsWith('document:')) {
      // The file itself, because there is nowhere else to send someone yet: a document has no
      // page of its own in this application. Fetched with the caller's token rather than
      // linked, since the content route will not answer a plain anchor.
      const fileId = value.slice('document:'.length);
      downloadFile(`/api/v1/files/${encodeURIComponent(fileId)}/content`).catch(() =>
        message.error(t('search.openFailed')),
      );
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
