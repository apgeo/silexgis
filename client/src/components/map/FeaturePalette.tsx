// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AppstoreOutlined, PushpinFilled, PushpinOutlined } from '@ant-design/icons';
import { Button, Popover, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FeatureType } from '../../api/hooks.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { groupFeatureTypes } from './featureTypeGroups.ts';

interface FeaturePaletteProps {
  featureTypes: FeatureType[];
  value?: number;
  onChange: (id: number) => void;
}

/**
 * Visual symbol picker for the edit tools: feature types grouped by geometry kind,
 * each shown as its actual map symbol. Reproduces the reference software's feature
 * palette; picking a symbol is what the caller uses to arm drawing for that type.
 * Each item carries a pushpin toggle that pins the type as a one-click toolbar shortcut.
 */
export default function FeaturePalette({ featureTypes, value, onChange }: FeaturePaletteProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const pinnedTypeIds = useUiPrefsStore((s) => s.pinnedTypeIds);
  const togglePinnedType = useUiPrefsStore((s) => s.togglePinnedType);
  const selected = featureTypes.find((ft) => Number(ft.id) === value);

  const groups = groupFeatureTypes(featureTypes);

  const content = (
    <div className="feature-palette">
      {groups.map((group) => (
        <div key={group.kind} className="feature-palette-group">
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {t(`mapEdit.groups.${group.kind}`)}
          </Typography.Text>
          <div className="feature-palette-grid">
            {group.items.map((ft) => {
              const id = Number(ft.id);
              const pinned = pinnedTypeIds.includes(id);
              return (
                <div key={id} className="feature-palette-item-wrap">
                  <button
                    type="button"
                    title={ft.name}
                    aria-label={ft.name}
                    aria-pressed={id === value}
                    className={`feature-palette-item${id === value ? ' is-selected' : ''}`}
                    onClick={() => {
                      onChange(id);
                      setOpen(false);
                    }}
                  >
                    <FeatureSymbol type={ft} />
                    <span className="feature-palette-label">{ft.name}</span>
                  </button>
                  <button
                    type="button"
                    title={pinned ? t('mapEdit.unpinType') : t('mapEdit.pinType')}
                    aria-label={pinned ? t('mapEdit.unpinType') : t('mapEdit.pinType')}
                    aria-pressed={pinned}
                    className={`feature-palette-pin${pinned ? ' is-pinned' : ''}`}
                    data-testid={`palette-pin-${id}`}
                    onClick={() => togglePinnedType(id)}
                  >
                    {pinned ? <PushpinFilled /> : <PushpinOutlined />}
                  </button>
                </div>
              );
            })}
          </div>
        </div>
      ))}
    </div>
  );

  return (
    <Popover
      content={content}
      title={t('mapEdit.pickFeatureType')}
      trigger="click"
      placement="topLeft"
      open={open}
      onOpenChange={setOpen}
    >
      <Button
        size="small"
        style={{ minWidth: 150, textAlign: 'left' }}
        icon={selected ? undefined : <AppstoreOutlined />}
        data-testid="feature-palette-trigger"
      >
        {selected ? (
          <span style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            <FeatureSymbol type={selected} size={16} />
            {selected.name}
          </span>
        ) : (
          t('mapEdit.featureType')
        )}
      </Button>
    </Popover>
  );
}

/** Renders a feature type's map symbol (its PNG), falling back to a plain dot. */
export function FeatureSymbol({ type, size = 26 }: { type: FeatureType; size?: number }) {
  if (type.symbolFile) {
    return (
      <img
        src={`/feature_symbols/${type.symbolFile}`}
        alt=""
        width={size}
        height={size}
        style={{ objectFit: 'contain' }}
      />
    );
  }
  return (
    <span
      style={{ display: 'inline-block', width: size * 0.5, height: size * 0.5, borderRadius: '50%', background: '#7a5c1e' }}
    />
  );
}
