// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useState } from 'react';
import { EditOutlined, ExportOutlined, EyeOutlined, SettingOutlined } from '@ant-design/icons';
import { Alert, App, Button, Empty, Flex, Popover, Skeleton, Switch, Tooltip, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useAnnotatedText, useResLinksForTarget, type ResLink } from '../api/hooks.ts';
import AddMemberModal, { type LinkOrigin } from '../components/reslinks/AddMemberModal.tsx';
import { memberRoute } from '../components/reslinks/registry.ts';
import { relationPhraseFor } from '../components/reslinks/relations.ts';
import { useDeleteResLink } from '../api/hooks.ts';
import { resolveDark } from '../theme.ts';
import { useUiPrefsStore } from '../stores/uiPrefsStore.ts';
import { useWorkspaceStore } from '../stores/workspaceStore.ts';
import type { ViewControlDescriptor } from '../viewlinks/resourceRef.ts';
import { refreshRoster, reveal, subscribeToRoster } from '../viewlinks/viewTargets.ts';
import AnnotatedTextView from './AnnotatedTextView.tsx';
import { canonicalText } from './blocks.ts';
import { highlightsFrom, refFor, type Highlight, type HighlightTarget } from './highlights.ts';
import LinkHoverCard from './LinkHoverCard.tsx';
import { anchorFor, type GlobalRange } from './ranges.ts';

/**
 * A link-annotated text, read beside the views it points into.
 *
 * This is the joinery: it fetches the words and the links, hands them to the reader, and decides
 * what following a passage means. The deciding is all it adds, and it is worth stating plainly
 * because it is the behaviour the whole feature exists for —
 *
 * <b>following a link moves the views, it does not replace the page.</b> A reader working through
 * a survey report beside a map wants the map to go to the passage's cave and wants to still be
 * reading the report afterwards. So the default is a reveal to every view that can answer, and
 * opening a target's own page is a separate control in the hover card that says so.
 *
 * Which views answer is a live question, not a setting: they are whatever is mounted in this
 * window plus whatever any other window announces, and a reader can mute any of them here.
 * Muting is per view and lasts as long as the panel is open, which is the honest lifetime —
 * the addresses it names are per session, so persisting them would restore a set of mutes
 * against views that no longer exist.
 */

export interface AnnotatedTextPanelProps {
  documentId: string;
  /** Drawn above the text — a title bar the embedding surface supplies. */
  header?: React.ReactNode;
  /** Offers the pop-out button. Off inside a pop-out, which is already one. */
  mayPopOut?: boolean;
}

export default function AnnotatedTextPanel({
  documentId,
  header,
  mayPopOut = true,
}: AnnotatedTextPanelProps) {
  const { t } = useTranslation();
  const { modal } = App.useApp();
  const navigate = useNavigate();
  const dark = useUiPrefsStore((s) => resolveDark(s.appearance.theme));
  const setSelection = useWorkspaceStore((s) => s.setSelection);

  const { data: text, isPending, isError, error } = useAnnotatedText(documentId);
  // Every link on the document in one read. The panel has no paging of its own: a passage the
  // page did not fetch would be text with no highlight on it, which reads as "nothing is linked
  // here" — the one answer that is worse than a slow load.
  const { data: linkPage } = useResLinksForTarget('document', documentId, { pageSize: 200 });
  const deleteLink = useDeleteResLink();

  const [editing, setEditing] = useState(false);
  const [muted, setMuted] = useState<ReadonlySet<string>>(new Set());
  const [controls, setControls] = useState<ViewControlDescriptor[]>([]);
  const [activeMemberId, setActiveMemberId] = useState<string | null>(null);
  const [pending, setPending] = useState<{ range: GlobalRange; quote: string } | null>(null);

  useEffect(() => {
    let alive = true;
    const read = () => {
      void refreshRoster().then((roster) => {
        if (alive) {
          setControls(roster);
        }
      });
    };

    read();
    const unsubscribe = subscribeToRoster(read);
    return () => {
      alive = false;
      unsubscribe();
    };
  }, []);

  const blocks = useMemo(() => text?.blocks ?? [], [text]);
  const stream = useMemo(() => canonicalText(blocks), [blocks]);

  const highlights = useMemo(
    () =>
      highlightsFrom(
        linkPage?.items ?? [],
        documentId,
        stream,
        // Read from this document's own side, so a directed relation says what it means to
        // the reader of the text rather than to whatever is on the other end of it.
        (link: ResLink) => relationPhraseFor(link, 'document', documentId, t),
      ),
    [linkPage, documentId, stream, t],
  );

  /** Where a target is sent when a passage is followed: every view that is not muted. */
  const revealEverywhere = useCallback(
    (target: HighlightTarget) => {
      // The workspace selection is set first and unconditionally. It is what the detail panel,
      // the history and the breadcrumb read, and it is not a fact about any view: making it
      // depend on one leaves a passage clicked where no view is open — the document's own page,
      // or a panel whose views the reader has muted — doing nothing whatsoever.
      if (target.targetType === 'feature') {
        setSelection({ kind: 'feature', featureId: target.targetId });
      }

      // With nothing muted the request names no controls at all, which the registry reads as
      // "every view that can show it". That is not a shortcut: naming them would mean sending
      // only to the roster this panel last gathered, so a click in the first moments after it
      // mounts would reach nothing, and a window opened since the last roll call would be
      // missed. Enumerating is needed only to leave a muted view out.
      const to = muted.size === 0
        ? undefined
        : controls.filter((c) => !muted.has(c.address)).map((c) => c.address);
      if (to === undefined || to.length > 0) {
        reveal(refFor(target), to);
      }
    },
    [controls, muted, setSelection],
  );

  const onActivate = useCallback(
    (highlight: Highlight) => {
      setActiveMemberId(highlight.memberId);
      for (const target of highlight.targets) {
        revealEverywhere(target);
      }
    },
    [revealEverywhere],
  );

  const onOpen = useCallback(
    (target: HighlightTarget, newWindow: boolean) => {
      const route = memberRoute(target.targetType, target.targetId, target.display);
      if (route === null) {
        return;
      }

      if (newWindow) {
        // `noopener` because the opened page is a page of this application and has no business
        // reaching back into the one that opened it.
        window.open(route, '_blank', 'noopener');
      } else {
        void navigate(route);
      }
    },
    [navigate],
  );

  const onDelete = useCallback(
    (highlight: Highlight) => {
      modal.confirm({
        title: t('textLink.edit.deleteTitle'),
        content: t('textLink.edit.deleteBody'),
        okButtonProps: { danger: true },
        onOk: () => deleteLink.mutateAsync(highlight.linkId),
      });
    },
    [modal, t, deleteLink],
  );

  if (isPending) {
    return <Skeleton active paragraph={{ rows: 8 }} style={{ padding: 16 }} />;
  }

  if (isError || text === undefined) {
    return (
      <Alert
        type="info"
        showIcon
        style={{ margin: 16 }}
        title={t('textLink.notAnnotated')}
        description={error instanceof Error ? error.message : undefined}
      />
    );
  }

  const origin: LinkOrigin | undefined =
    pending === null
      ? undefined
      : {
          targetType: 'document',
          targetId: documentId,
          title: text.title,
          anchor: {
            anchorKind: 'textRange',
            // Composed from the stream on screen rather than from the selection's own offsets,
            // so the quote and the context stored are the words as this revision has them.
            anchor: anchorFor(stream, pending.range),
            // The revision the passage was measured against. Without it a later edit could not
            // tell an anchor that survived from one that was never re-measured.
            anchorFileId: text.fileId,
          },
        };

  return (
    <Flex vertical style={{ height: '100%', minHeight: 0 }}>
      <Flex align="center" gap={4} style={{ padding: '6px 12px', flex: '0 0 auto' }} wrap>
        {header}
        <div style={{ flex: 1 }} />

        <Popover
          trigger="click"
          placement="bottomRight"
          content={
            <ControlList controls={controls} muted={muted} onToggle={setMuted} />
          }
        >
          <Tooltip title={t('textLink.controls.title')}>
            <Button size="small" type="text" icon={<SettingOutlined />} aria-label={t('textLink.controls.title')} />
          </Tooltip>
        </Popover>

        {text.mayWrite && (
          <Tooltip title={editing ? t('textLink.edit.stop') : t('textLink.edit.start')}>
            <Button
              size="small"
              type={editing ? 'primary' : 'text'}
              icon={editing ? <EyeOutlined /> : <EditOutlined />}
              aria-label={editing ? t('textLink.edit.stop') : t('textLink.edit.start')}
              onClick={() => setEditing((on) => !on)}
            />
          </Tooltip>
        )}

        {mayPopOut && (
          <Tooltip title={t('textLink.popOut')}>
            <Button
              size="small"
              type="text"
              icon={<ExportOutlined />}
              aria-label={t('textLink.popOut')}
              onClick={() =>
                window.open(
                  `/panel/text?document=${encodeURIComponent(documentId)}`,
                  `silexgis-text-${documentId}`,
                  'popup,width=620,height=900',
                )}
            />
          </Tooltip>
        )}
      </Flex>

      {editing && (
        <Alert
          type="info"
          showIcon
          banner
          title={t('textLink.edit.hint')}
          style={{ flex: '0 0 auto' }}
        />
      )}

      <div style={{ flex: 1, minHeight: 0, overflow: 'auto', padding: '4px 12px 16px' }}>
        {blocks.length === 0 ? (
          <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('textLink.empty')} />
        ) : (
          <AnnotatedTextView
            blocks={blocks}
            highlights={highlights}
            dark={dark}
            activeMemberId={activeMemberId}
            onActivate={onActivate}
            onSelectPassage={editing ? (range, quote) => setPending({ range, quote }) : undefined}
            renderCard={(covering) => (
              <LinkHoverCard
                highlights={covering}
                onReveal={revealEverywhere}
                onRevealIn={(target, address) => reveal(refFor(target), [address])}
                onOpen={onOpen}
                onDelete={editing ? onDelete : undefined}
              />
            )}
          />
        )}
      </div>

      {origin !== undefined && (
        <AddMemberModal
          open
          origin={origin}
          onClose={() => setPending(null)}
          onCreated={() => setPending(null)}
        />
      )}
    </Flex>
  );
}

/** The views this passage's links can be sent to, each with its own switch. */
function ControlList({
  controls,
  muted,
  onToggle,
}: {
  controls: ViewControlDescriptor[];
  muted: ReadonlySet<string>;
  onToggle: (next: ReadonlySet<string>) => void;
}) {
  const { t } = useTranslation();

  if (controls.length === 0) {
    // Said rather than left blank. A reader whose clicks appear to do nothing needs to know that
    // there is no view open to send them to, which is a different problem from a broken link.
    return (
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('textLink.controls.none')}
      </Typography.Text>
    );
  }

  return (
    <Flex vertical gap={6} style={{ minWidth: 220 }}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {t('textLink.controls.hint')}
      </Typography.Text>
      {controls.map((control) => (
        <Flex key={control.address} align="center" justify="space-between" gap={12}>
          <Typography.Text style={{ fontSize: 13 }}>
            {t(control.labelKey)}
            {control.local === false && (
              <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                {' '}
                — {t('textLink.card.otherWindow')}
              </Typography.Text>
            )}
          </Typography.Text>
          <Switch
            size="small"
            checked={!muted.has(control.address)}
            aria-label={t(control.labelKey)}
            onChange={(on) => {
              const next = new Set(muted);
              if (on) {
                next.delete(control.address);
              } else {
                next.add(control.address);
              }

              onToggle(next);
            }}
          />
        </Flex>
      ))}
    </Flex>
  );
}
