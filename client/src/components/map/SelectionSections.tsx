// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ReactNode } from 'react';
import { Button, Empty, Flex, Tooltip, Typography } from 'antd';
import { SettingOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import AttachmentSection from '../attachments/AttachmentSection.tsx';
import HistoryPanel, { type HistoryRestoreProp } from '../history/HistoryPanel.tsx';
import LinksSection from '../reslinks/LinksSection.tsx';
import TagChips from '../tags/TagChips.tsx';
import TextSection from '../../textlink/TextSection.tsx';
import PanelSection from './PanelSection.tsx';
import { usePanelLayout } from './usePanelLayout.ts';
import type { PanelSectionId } from './panelSections.ts';
import type { PanelScope } from '../../stores/panelPrefs.ts';

/** What one selected object gives the sections to work with. */
export interface SectionSubject {
  /** The feature the sections hang off — an entrance links itself, not the cave it belongs to. */
  entityId: string;
  entityTitle: string;
  canEdit: boolean;
  /** The details section is per-kind and is supplied by the card that knows the shape. */
  details: ReactNode;
  history?: HistoryRestoreProp;
}

/**
 * Draws one selected object's sections, in this panel's order, skipping the ones switched off and
 * fetching nothing for the ones that are closed.
 *
 * Every card in the panel goes through here, so "details, then tags, then attachments…" is one
 * arrangement rather than one per kind of object — which is what the owner asked for, and also the
 * only way a stored order can mean anything: an order that applied to caves but not to springs
 * would have to be stored per kind and re-taught every time a kind was added.
 */
export default function SelectionSections({
  scope,
  subject,
}: {
  scope: PanelScope;
  subject: SectionSubject;
}) {
  const { t } = useTranslation();
  const layout = usePanelLayout(scope);

  const render = (id: PanelSectionId): ReactNode => {
    const open = layout.isOpen(id);
    switch (id) {
      case 'details':
        return subject.details;
      case 'tags':
        return <TagChips entityType="feature" entityId={subject.entityId} canEdit={subject.canEdit} />;
      case 'attachments':
        return (
          <AttachmentSection
            entityType="feature"
            entityId={subject.entityId}
            canEdit={subject.canEdit}
            variant="bare"
            enabled={open}
          />
        );
      case 'links':
        return (
          <LinksSection
            entityType="feature"
            entityId={subject.entityId}
            variant="compact"
            canAdd
            open={open}
            entityTitle={subject.entityTitle}
          />
        );
      case 'text':
        // Closed by default and fetching nothing while it is: the reader inside it is a whole
        // second surface, and most selected objects have no annotated text written about them.
        return (
          <TextSection
            entityType="feature"
            entityId={subject.entityId}
            entityTitle={subject.entityTitle}
            open={open}
          />
        );
      case 'history':
        return (
          <HistoryPanel
            entityType="feature"
            entityId={subject.entityId}
            enabled={open}
            variant="bare"
            restore={subject.history}
          />
        );
      case 'permissions':
        // The id exists so the ordering vocabulary is complete; permissions is a modal everywhere
        // else in the application and stays one, launched from the panel header.
        return null;
    }
  };

  return (
    <div data-testid="selection-sections">
      {layout.visible.map((id) => {
        const body = render(id);
        if (body === null) {
          return null;
        }
        return (
          <PanelSection
            key={id}
            id={id}
            title={t(`panel.sections.${id}`)}
            open={layout.isOpen(id)}
            onToggle={() => layout.toggle(id)}
            onHide={() => layout.setHidden(id, true)}
            onMove={(direction) => layout.nudge(id, direction)}
            {...layout.dragHandlers(id)}
          >
            {body}
          </PanelSection>
        );
      })}

      {layout.visible.length === 0 && (
        <Flex vertical align="center" gap={8} style={{ padding: 16 }}>
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('panel.allHidden')} />
          <Button size="small" onClick={layout.reset}>
            {t('panel.restoreSections')}
          </Button>
        </Flex>
      )}
    </div>
  );
}

/** The control that brings hidden sections back and resets the arrangement. */
export function PanelSectionsMenu({ scope }: { scope: PanelScope }) {
  const { t } = useTranslation();
  const layout = usePanelLayout(scope);

  if (layout.hidden.size === 0) {
    return null;
  }

  return (
    <Flex gap={4} align="center" wrap style={{ padding: '4px 0' }}>
      <Typography.Text type="secondary" style={{ fontSize: 11 }}>
        {t('panel.hiddenSections')}
      </Typography.Text>
      {[...layout.hidden].map((id) => (
        <Tooltip key={id} title={t('panel.showSection')}>
          <Button size="small" type="dashed" onClick={() => layout.setHidden(id, false)}>
            {t(`panel.sections.${id}`)}
          </Button>
        </Tooltip>
      ))}
      <Tooltip title={t('panel.restoreSections')}>
        <Button
          size="small"
          type="text"
          icon={<SettingOutlined />}
          aria-label={t('panel.restoreSections')}
          onClick={layout.reset}
        />
      </Tooltip>
    </Flex>
  );
}
