// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { App, Button, Empty, Flex, Segmented, Typography } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { useTranslation } from 'react-i18next';
import {
  useCan,
  useCreateAnnotatedText,
  useCreateResLink,
  useResLinkRelationTypes,
  useResLinksForTarget,
} from '../api/hooks.ts';
import { ANNOTATED_TEXT_MEDIA_TYPE } from './mediaType.ts';
import AnnotatedTextPanel from './AnnotatedTextPanel.tsx';

/**
 * The annotated texts written about the selected object, in the panel beside the map.
 *
 * <b>Which documents these are is the server's statement, not a guess.</b> A link member's display
 * carries the format of the file its target serves, so the section knows which of a cave's linked
 * documents is a piece of annotated text and which is a scan of a 1974 report — without asking
 * about each of them in turn, and without the alternative that was actually tempting: opening
 * every linked document in the reader and letting the ones that are not annotated texts say so.
 * That alternative costs a request per document and puts a refusal notice on the screen for every
 * ordinary attachment, which reads as breakage.
 *
 * The link read is issued with the same parameters the links section on the same panel already
 * uses, so the two share a cache key and this section costs no extra request. Changing the page
 * size in one place and not the other silently doubles the panel's link traffic.
 */
export default function TextSection({
  entityType,
  entityId,
  entityTitle,
  open,
}: {
  entityType: string;
  entityId: string;
  /** Names the text this section creates, so a new one is not called "Untitled". */
  entityTitle?: string;
  /** False while the section is collapsed: a closed section fetches nothing. */
  open: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data } = useResLinksForTarget(entityType, entityId, {}, open);
  const { data: relations } = useResLinkRelationTypes(open);
  const createText = useCreateAnnotatedText();
  const createLink = useCreateResLink();
  const mayCreate = useCan('documents', 'create');
  const [chosen, setChosen] = useState<string | null>(null);

  /**
   * Writes a new text about this object and links it to it in one gesture.
   *
   * Two calls rather than one, and deliberately: the text is a document created through the
   * document rules, and the link is a link created through the link rules. A single endpoint
   * that did both would be a second place where "who may bind this to that club" and "who may
   * add a member to a link" are decided.
   *
   * If the link fails the text still exists — unfiled and readable by its author, which is what
   * any document with no cabinet is. That is recoverable; the alternative, deleting a document
   * somebody's words are now in because a second call failed, is not.
   */
  const write = async () => {
    try {
      const created = await createText.mutateAsync({
        title: entityTitle ? t('textLink.section.newTitle', { name: entityTitle }) : t('textLink.section.newTitleBare'),
        blocks: [{ type: 'p', text: t('textLink.section.newBody') }],
        visibility: 'private',
        cavingGroupId: null,
      });

      await createLink.mutateAsync({
        relationTypeId: relations?.find((r) => r.code === 'documents')?.id ?? null,
        description: null,
        members: [
          // "Documented by" reads from the thing being described, so the marker sits on it.
          { targetType: entityType, targetId: entityId, isMain: true, sortOrder: 0, note: null, anchorKind: 'whole', anchor: null, anchorFileId: null },
          { targetType: 'document', targetId: created.documentId, isMain: false, sortOrder: 1, note: null, anchorKind: 'whole', anchor: null, anchorFileId: null },
        ],
      });

      setChosen(created.documentId);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const texts = useMemo(() => {
    const found = new Map<string, string>();
    for (const link of data?.items ?? []) {
      for (const member of link.members) {
        if (
          member.targetType === 'document'
          && member.display?.mediaType === ANNOTATED_TEXT_MEDIA_TYPE
          && !found.has(member.targetId)
        ) {
          found.set(member.targetId, member.display.title);
        }
      }
    }

    return [...found].map(([id, title]) => ({ id, title }));
  }, [data]);

  if (!open) {
    return null;
  }

  if (texts.length === 0) {
    return (
      <Flex vertical align="center" gap={8} style={{ padding: 8 }}>
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('textLink.section.none')} />
        {mayCreate && (
          <Button
            size="small"
            icon={<PlusOutlined />}
            loading={createText.isPending || createLink.isPending}
            onClick={() => void write()}
          >
            {t('textLink.section.write')}
          </Button>
        )}
      </Flex>
    );
  }

  // The first is shown rather than none: a section that made somebody pick before it showed
  // anything would be a section that is empty most of the time it is open.
  const active = texts.find((text) => text.id === chosen) ?? texts[0];

  return (
    <Flex vertical gap={8} style={{ minHeight: 0 }}>
      {texts.length > 1 && (
        <Segmented
          size="small"
          value={active.id}
          onChange={(value) => setChosen(String(value))}
          options={texts.map((text) => ({ label: text.title, value: text.id }))}
        />
      )}
      {texts.length === 1 && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {active.title}
        </Typography.Text>
      )}
      <AnnotatedTextPanel documentId={active.id} />
    </Flex>
  );
}
