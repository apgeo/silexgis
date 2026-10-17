// SPDX-License-Identifier: AGPL-3.0-or-later
import { UnorderedListOutlined } from '@ant-design/icons';
import { App, Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { documentOf, useSavedFilters } from '../../api/hooks/savedFilters.ts';
import { registryAddressForMapView } from '../../filters/tableMapHandoff.ts';
import { mapLonLatExtent } from '../../map/mapContext.ts';
import type { MapFilter } from '../../map/mapFilters.ts';
import Button from '../Button.tsx';

/**
 * "List what is in view": the cave registry, asked the map's own question inside the map's window.
 *
 * The registry is handed the map's filter as a document — the saved filter's document when the
 * map runs a saved one, with its id so the registry shows it as applied — and the window as a box
 * the server decides membership in. A saved filter this reader cannot read has no document to hand
 * over; the button says so rather than opening a registry that would list more than the map shows.
 */
export default function MapListInViewButton({ filter }: { filter: MapFilter | null }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { data: savedFilters } = useSavedFilters(filter?.kind === 'saved');

  const open = () => {
    const extent = mapLonLatExtent();
    if (!extent) {
      return;
    }
    if (filter === null) {
      navigate(registryAddressForMapView(extent, null));
      return;
    }
    if (filter.kind === 'document') {
      navigate(registryAddressForMapView(extent, filter.document));
      return;
    }
    const saved = savedFilters?.find((row) => row.id === filter.savedFilterId);
    const document = saved === undefined ? null : documentOf(saved);
    if (document === null) {
      message.warning(t('tableMap.savedFilterNotListable'));
      return;
    }
    navigate(registryAddressForMapView(extent, document, filter.savedFilterId));
  };

  return (
    <Tooltip title={t('tableMap.listInView')}>
      <Button
        size="small"
        className="map-list-in-view"
        icon={<UnorderedListOutlined />}
        aria-label={t('tableMap.listInView')}
        data-testid="map-list-in-view"
        onClick={open}
      />
    </Tooltip>
  );
}
