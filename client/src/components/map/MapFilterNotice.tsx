// SPDX-License-Identifier: AGPL-3.0-or-later
import { CloseOutlined } from '@ant-design/icons';
import { Alert, Flex } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSavedFilters } from '../../api/hooks/savedFilters.ts';
import { tagOfMapFilter, type MapFilter } from '../../map/mapFilters.ts';
import Button from '../Button.tsx';

interface MapFilterNoticeProps {
  filter: MapFilter | null;
  /** A filter arrived in the address and could not be read; the map opened unfiltered. */
  unreadableLink: boolean;
  onDismissUnreadableLink: () => void;
  /** The server refused the filter, so the layers it narrows show nothing. */
  refused: boolean;
  onClear: () => void;
}

/**
 * What the map's filter is doing, said on the map itself.
 *
 * A filter that arrives by a link or a saved view would otherwise be invisible: the layer panel's
 * tag chooser can show a tag, and nothing else. A map that silently shows fewer caves than the
 * person expects is a map they stop trusting, and one that silently shows nothing because the
 * filter was refused is worse — so both say so, with the way out beside the sentence.
 */
export default function MapFilterNotice({
  filter,
  unreadableLink,
  onDismissUnreadableLink,
  refused,
  onClear,
}: MapFilterNoticeProps) {
  const { t } = useTranslation();
  const isSaved = filter?.kind === 'saved';
  const { data: savedFilters } = useSavedFilters(isSaved);

  const clear = (
    <Button size="small" onClick={onClear} data-testid="map-filter-clear">
      {t('mapFilter.clear')}
    </Button>
  );

  let active: string | null = null;
  if (filter !== null && !refused && tagOfMapFilter(filter) === null) {
    if (filter.kind === 'saved') {
      const saved = savedFilters?.find((f) => f.id === filter.savedFilterId);
      active = t('mapFilter.active', { name: saved?.name ?? t('mapFilter.savedUnavailable') });
    } else {
      active = t('mapFilter.activeUnnamed');
    }
  }

  if (!unreadableLink && !refused && active === null) {
    return null;
  }

  return (
    <Flex vertical gap={4} className="map-filter-notice" data-testid="map-filter-notice">
      {unreadableLink && (
        <Alert
          type="warning"
          showIcon
          title={t('mapFilter.unreadableLink')}
          // The icon is named although it is the library's own default: its alert draws a close
          // button for an options object only when that object carries one.
          closable={{ closeIcon: <CloseOutlined />, onClose: onDismissUnreadableLink, 'aria-label': t('common.close') }}
          data-testid="map-filter-unreadable"
        />
      )}
      {refused && (
        <Alert type="error" showIcon title={t('mapFilter.refused')} action={clear} data-testid="map-filter-refused" />
      )}
      {active !== null && <Alert type="info" showIcon title={active} action={clear} data-testid="map-filter-active" />}
    </Flex>
  );
}
