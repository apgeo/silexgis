// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { AppstoreOutlined } from '@ant-design/icons';
import { Button, Popover, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { FeatureType } from '../../api/hooks.ts';

interface FeaturePaletteProps {
  featureTypes: FeatureType[];
  value?: number;
  onChange: (id: number) => void;
}

// Geometry-kind groups, rendered in this order. `any` collects the flexible types.
const GROUP_ORDER = ['point', 'line', 'polygon', 'any'] as const;

/**
 * Visual symbol picker for the edit tools: feature types grouped by geometry kind,
 * each shown as its actual map symbol. Reproduces the reference software's feature
 * palette; picking a symbol is what the caller uses to arm drawing for that type.
 */
export default function FeaturePalette({ featureTypes, value, onChange }: FeaturePaletteProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const selected = featureTypes.find((ft) => Number(ft.id) === value);

  const groups = GROUP_ORDER.map((kind) => ({
    kind,
    items: featureTypes.filter((ft) => (ft.geometryKind ?? 'point') === kind),
  })).filter((group) => group.items.length > 0);

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
              return (
                <button
                  key={id}
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
      <Button size="small" style={{ minWidth: 150, textAlign: 'left' }} icon={selected ? undefined : <AppstoreOutlined />}>
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
function FeatureSymbol({ type, size = 26 }: { type: FeatureType; size?: number }) {
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
