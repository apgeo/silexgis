// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState, type ReactNode } from 'react';
import { SearchOutlined } from '@ant-design/icons';
import { App, AutoComplete, Input, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useNominatim, useSearch, type FeatureKind } from '../../api/hooks.ts';
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
    // The document, not the file that currently carries it: a hit against a superseded
    // revision still belongs to the same document, and the page is addressed by document.
    //
    // The matched position rides along only where it indexes the pictures the reader will
    // actually be shown — which is what a division of "page" means: the words were read off
    // the very artifact those pictures are drawn from, whether that is a PDF somebody uploaded
    // or the portable copy an installation made of an office document. A sheet or a slide is a
    // real division of the upload and is named as one beside the hit, but nothing has laid
    // those out into pages here, so there is no page to open at and the document opens at its
    // beginning. Better to let the reader look than to send them somewhere wrong.
    value:
      hit.division === 'page'
        ? `document:${hit.id}:${hit.pageNumber}`
        : `document:${hit.id}`,
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
      // The document's own page, opened at the passage that matched where there is one to
      // open at. Searching for a sentence and being handed a file to download was never
      // reading it — and it threw away the one thing the hit knew.
      const [, documentId, pageNumber] = value.split(':');
      navigate(
        pageNumber === undefined
          ? `/documents/${encodeURIComponent(documentId)}`
          : `/documents/${encodeURIComponent(documentId)}?page=${encodeURIComponent(pageNumber)}`,
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
