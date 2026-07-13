// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo } from 'react';
import { App, Dropdown } from 'antd';
import type { MenuProps } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FeatureType } from '../../api/hooks.ts';
import type { MapContextMenuTarget } from '../../map/contextMenu.ts';
import type { PlacementMode } from '../../map/mapEdit.ts';
import { FeatureSymbol } from './FeaturePalette.tsx';
import { groupFeatureTypes } from './featureTypeGroups.ts';

interface MapContextMenuProps {
  /** Where the menu is anchored/aimed; null keeps it closed. */
  target: MapContextMenuTarget | null;
  featureTypes: FeatureType[];
  canEdit: boolean;
  onClose: () => void;
  /** A feature type was picked for "add here". */
  onAddFeature: (typeId: number, lonLat: [number, number]) => void;
  /** "New cave/entrance here" was picked. */
  onPlace: (mode: PlacementMode, lonLat: [number, number]) => void;
}

/**
 * Right-click menu over the map canvas: typed "add feature here" submenus
 * (mirroring the palette's grouping), cave/entrance placement and coordinate
 * copy. Anchored to an invisible point at the click pixel; the caller owns the
 * open state and the armed edit tools.
 */
export default function MapContextMenu({
  target,
  featureTypes,
  canEdit,
  onClose,
  onAddFeature,
  onPlace,
}: MapContextMenuProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();

  const items = useMemo<MenuProps['items']>(() => {
    const list: NonNullable<MenuProps['items']> = [];
    if (canEdit) {
      list.push({
        key: 'add-feature',
        label: t('map.contextAdd'),
        children: groupFeatureTypes(featureTypes).map((group) => ({
          key: `group-${group.kind}`,
          type: 'group' as const,
          label: t(`mapEdit.groups.${group.kind}`),
          children: group.items.map((ft) => ({
            key: `type-${ft.id}`,
            label: ft.name,
            icon: <FeatureSymbol type={ft} size={16} />,
          })),
        })),
      });
      list.push({ key: 'add-cave', label: t('map.contextNewCave') });
      list.push({ key: 'add-entrance', label: t('map.contextNewEntrance') });
      list.push({ type: 'divider' });
    }
    list.push({ key: 'copy-coords', label: t('map.contextCopyCoords') });
    return list;
  }, [canEdit, featureTypes, t]);

  const onClick: MenuProps['onClick'] = ({ key }) => {
    if (!target) {
      return;
    }
    if (key.startsWith('type-')) {
      onAddFeature(Number(key.slice('type-'.length)), target.lonLat);
    } else if (key === 'add-cave' || key === 'add-entrance') {
      onPlace(key, target.lonLat);
    } else if (key === 'copy-coords') {
      const [lon, lat] = target.lonLat;
      void navigator.clipboard
        ?.writeText(`${lat.toFixed(6)}, ${lon.toFixed(6)}`)
        .then(() => message.success(t('map.coordsCopied')));
    }
    onClose();
  };

  return (
    <Dropdown
      open={target !== null}
      trigger={[]}
      // Dropdown layers stack above modals; tear the whole tree down on close so a
      // lingering submenu popup can never sit over the dialog a pick just opened.
      destroyOnHidden
      menu={{ items, onClick }}
      onOpenChange={(open) => {
        if (!open) {
          onClose();
        }
      }}
      placement="bottomLeft"
    >
      <div
        className="map-context-anchor"
        data-testid="map-context-anchor"
        style={target ? { left: target.pixel[0], top: target.pixel[1] } : { display: 'none' }}
      />
    </Dropdown>
  );
}
