// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { DownOutlined, EyeInvisibleOutlined, HolderOutlined, RightOutlined } from '@ant-design/icons';
import { Button, Flex, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { PanelSectionId } from './panelSections.ts';

interface Props {
  id: PanelSectionId;
  title: string;
  open: boolean;
  onToggle: () => void;
  onHide: () => void;
  /** Drag-to-reorder, and the keyboard equivalent for anyone not using a mouse. */
  onMove: (direction: -1 | 1) => void;
  onDragStart: () => void;
  onDragOver: () => void;
  onDrop: () => void;
  dragging: boolean;
  children: ReactNode;
}

/**
 * One section of the selection panel: a header that is always drawn, and a body that is drawn only
 * while the section is open.
 *
 * The header owns the chrome — the disclosure arrow, the drag handle, the hide button — rather than
 * each section drawing its own. That is what makes reordering possible at all: a section whose body
 * renders nothing (a links list with no links) still has a row to grab, where a section drawing its
 * own header would simply vanish and leave a hole in the list somebody is trying to drag through.
 *
 * Icon buttons rather than labelled ones, deliberately: this panel is the narrowest surface in the
 * application and three words of chrome per section is a scrollbar.
 */
export default function PanelSection({
  id,
  title,
  open,
  onToggle,
  onHide,
  onMove,
  onDragStart,
  onDragOver,
  onDrop,
  dragging,
  children,
}: Props) {
  const { t } = useTranslation();

  return (
    <section
      data-testid={`panel-section-${id}`}
      data-open={open}
      // The whole section is the drop target, not just the header: a half-height drop zone is the
      // difference between reordering working and reordering appearing broken.
      onDragOver={(event) => {
        event.preventDefault();
        onDragOver();
      }}
      onDrop={(event) => {
        event.preventDefault();
        onDrop();
      }}
      style={{ opacity: dragging ? 0.5 : 1, marginBottom: 4 }}
    >
      <Flex align="center" gap={2} style={{ minHeight: 28 }}>
        <Tooltip title={t('panel.reorderHint')}>
          <span
            draggable
            onDragStart={onDragStart}
            aria-label={t('panel.reorder', { section: title })}
            role="button"
            tabIndex={0}
            // Alt+arrows move a section without a pointer. The same two keys the reorder tooltip
            // names, so the hint is true for both ways of doing it.
            onKeyDown={(event) => {
              if (event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
                event.preventDefault();
                onMove(event.key === 'ArrowUp' ? -1 : 1);
              }
            }}
            style={{ cursor: 'grab', color: 'var(--silexgis-muted, #8c8c8c)', padding: '0 2px' }}
          >
            <HolderOutlined />
          </span>
        </Tooltip>

        <Button
          type="text"
          size="small"
          onClick={onToggle}
          aria-expanded={open}
          aria-controls={`panel-section-body-${id}`}
          icon={open ? <DownOutlined /> : <RightOutlined />}
          style={{ flex: 1, justifyContent: 'flex-start', paddingInline: 4 }}
        >
          <Typography.Text strong style={{ fontSize: 13 }}>
            {title}
          </Typography.Text>
        </Button>

        <Tooltip title={t('panel.hideSection')}>
          <Button
            type="text"
            size="small"
            icon={<EyeInvisibleOutlined />}
            aria-label={t('panel.hideSectionNamed', { section: title })}
            onClick={onHide}
          />
        </Tooltip>
      </Flex>

      {/* Unmounted rather than hidden. That is the whole of "a collapsed section fetches nothing":
          the queries live inside these children, so a section that is merely display:none would
          still be asking the server for a cave's history nobody is looking at. */}
      {open && <div id={`panel-section-body-${id}`}>{children}</div>}
    </section>
  );
}
