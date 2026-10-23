// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { CloseOutlined, WarningOutlined } from '@ant-design/icons';
import { Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSavedFilters, type SavedFilter } from '../../api/hooks/savedFilters.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import Button from '../Button.tsx';
import { quickChipStatus } from './quickChipStatus.ts';
import './QuickFilterChips.css';

interface Props {
  /** The saved filter the surface is drawing through now, if any; its chip shows pressed. */
  activeId?: string | null;
  /** A usable chip was pressed. */
  onApply: (filter: SavedFilter) => void;
  /** The pressed chip was pressed again: the surface goes back to unfiltered. */
  onRelease: () => void;
  className?: string;
  'data-testid'?: string;
}

/**
 * The account's pinned saved filters as a row of one-click chips.
 *
 * The pins are the account's, kept with its preferences, so the same chips follow the person to
 * another browser and are not handed to the next member who signs in on this one. A chip names a
 * filter by id only; what it shows, and whether it can still be run, is read from the saved-filter
 * list under this account's own access, so a chip never shows a name the person can no longer read.
 */
export default function QuickFilterChips({ activeId, onApply, onRelease, className, ...rest }: Props) {
  const { t } = useTranslation();
  const pinned = useUiPrefsStore((s) => s.pinnedFilterIds);
  const togglePinnedFilter = useUiPrefsStore((s) => s.togglePinnedFilter);
  const { data: filters } = useSavedFilters(pinned.length > 0);
  const group = useRef<HTMLDivElement>(null);
  // After an unpin, focus goes to the chip that took its place — or to the row's empty line —
  // instead of being dropped onto the page with the button that held it.
  const [focusAfterUnpin, setFocusAfterUnpin] = useState<string | null | undefined>(undefined);
  const [unpinnedHere, setUnpinnedHere] = useState(false);

  useEffect(() => {
    if (focusAfterUnpin === undefined) {
      return;
    }
    const target = focusAfterUnpin === null
      ? group.current?.querySelector<HTMLElement>('[data-quick-chip-empty]')
      : group.current?.querySelector<HTMLElement>(`[data-quick-chip="${focusAfterUnpin}"]`);
    target?.focus();
    setFocusAfterUnpin(undefined);
  }, [focusAfterUnpin, pinned]);

  if (pinned.length === 0 && !unpinnedHere) {
    return null;
  }

  const unpin = (id: string) => {
    const at = pinned.indexOf(id);
    const remaining = pinned.filter((x) => x !== id);
    setUnpinnedHere(true);
    // A filter applied through this chip stays applied: unpinning takes a shortcut away, not the
    // filter on screen.
    togglePinnedFilter(id);
    setFocusAfterUnpin(remaining.length === 0 ? null : (remaining[Math.min(at, remaining.length - 1)] ?? null));
  };

  return (
    <div
      ref={group}
      role="group"
      aria-label={t('quickFilters.label')}
      className={['quick-filter-chips', className].filter(Boolean).join(' ')}
      data-testid={rest['data-testid']}
    >
      {pinned.length === 0 && (
        <Typography.Text type="secondary" tabIndex={-1} data-quick-chip-empty className="quick-filter-chips-empty">
          {t('quickFilters.none')}
        </Typography.Text>
      )}
      {pinned.map((id) => {
        const status = quickChipStatus(id, filters);
        const filter = filters?.find((candidate) => candidate.id.toLowerCase() === id);
        const usable = status === 'ready' || status === 'stale';
        const pressed = usable && activeId?.toLowerCase() === id;
        const name = status === 'gone'
          ? t('quickFilters.gone')
          : status === 'loading'
            ? '…'
            : filter!.name;
        const label = status === 'unreadable' ? t('quickFilters.unreadable', { name }) : name;
        return (
          <span key={id} className={`quick-filter-chip is-${status}`} data-testid={`quick-filter-${id}`}>
            <Button
              size="small"
              shape="round"
              type={pressed ? 'primary' : 'default'}
              aria-pressed={usable ? pressed : undefined}
              disabled={!usable}
              icon={usable || status === 'loading' ? undefined : <WarningOutlined />}
              // The name may be cut short on screen; the whole of it is said on hover.
              title={status === 'stale' ? t('savedFilters.staleHint') : label}
              data-quick-chip={id}
              data-testid={`quick-filter-apply-${id}`}
              onClick={() => {
                if (!filter || !usable) return;
                if (pressed) {
                  onRelease();
                } else {
                  onApply(filter);
                }
              }}
            >
              <span className="quick-filter-chip-label">{label}</span>
            </Button>
            <Button
              size="small"
              type="text"
              className="quick-filter-chip-unpin"
              icon={<CloseOutlined />}
              aria-label={t('quickFilters.unpin', { name })}
              title={t('quickFilters.unpin', { name })}
              data-testid={`quick-filter-unpin-${id}`}
              onClick={() => unpin(id)}
            />
          </span>
        );
      })}
    </div>
  );
}
