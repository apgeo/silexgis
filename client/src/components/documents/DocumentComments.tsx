// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, EditOutlined } from '@ant-design/icons';
import { App, Button, Empty, Flex, Input, Popconfirm, Space, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateDocumentComment,
  useDeleteDocumentComment,
  useDocumentComments,
  useUpdateDocumentComment,
  type DocumentComment,
} from '../../api/hooks.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';

/** Matches the server's cap on a comment body, so the counter and the refusal agree. */
const MAX_BODY = 4000;

/**
 * The discussion on a document.
 *
 * Two things about this panel are load-bearing and easy to undo by accident.
 *
 * The first is that a comment body is **plain text, rendered as plain text**. It is the one
 * place in this application where prose one member typed is shown to another, so it is
 * placed in the tree as a text node and never as markup: line breaks survive through
 * `white-space: pre-wrap` and long unbroken strings through `overflow-wrap`, and nothing
 * between the database and the screen is tempted to interpret what was typed. Adding a
 * Markdown or HTML renderer here would turn every remark into a way of putting elements in
 * somebody else's page.
 *
 * The second is that `mayEdit` and `mayDelete` come from the server, per comment. This
 * component never compares the reader's identity to an author id to decide what to offer:
 * that rule has an administrator branch in it and lives on the server, which re-asks it
 * before acting whatever this panel drew.
 *
 * The composer, by contrast, is offered to everyone this panel is drawn for, and that is the
 * rule rather than an oversight: posting asks for nothing beyond a signed-in account, so any
 * member who can open a document may join the discussion on it, whether or not they may
 * change the document itself. The panel only ever renders behind the signed-in part of the
 * application, so "is there an account" is already answered before it mounts; hiding or
 * disabling the box would therefore be a second, weaker copy of a rule the server holds, and
 * would tell members who are entitled to write that they are not.
 *
 * Every control is a permanently visible button rather than something revealed by hovering,
 * because the same panel is used on a phone where there is no hover.
 */
export default function DocumentComments({ documentId }: { documentId: string }) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const { data, isPending, isError } = useDocumentComments(documentId);
  const create = useCreateDocumentComment();
  const update = useUpdateDocumentComment();
  const remove = useDeleteDocumentComment();

  const [draft, setDraft] = useState('');
  const [replyTo, setReplyTo] = useState<string | null>(null);
  const [replyDraft, setReplyDraft] = useState('');
  const [editing, setEditing] = useState<string | null>(null);
  const [editDraft, setEditDraft] = useState('');

  if (isError) {
    return <Typography.Text type="secondary">{t('common.loadFailed')}</Typography.Text>;
  }
  if (isPending) {
    return <Spin />;
  }

  const comments = data.items;
  const roots = comments.filter((c) => c.parentId === null);
  const repliesOf = (id: string) => comments.filter((c) => c.parentId === id);

  const post = async (body: string, parentId: string | null) => {
    const trimmed = body.trim();
    if (!trimmed) {
      return;
    }
    try {
      await create.mutateAsync({ documentId, parentId, body: trimmed });
      if (parentId === null) {
        setDraft('');
      } else {
        setReplyTo(null);
        setReplyDraft('');
      }
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const save = async (id: string) => {
    const trimmed = editDraft.trim();
    if (!trimmed) {
      return;
    }
    try {
      await update.mutateAsync({ documentId, id, body: trimmed });
      setEditing(null);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const drop = async (id: string) => {
    try {
      await remove.mutateAsync({ documentId, id });
      message.success(t('common.deleted'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const line = (comment: DocumentComment) => {
    const written = new Date(comment.createdAt).toLocaleString(i18n.resolvedLanguage);
    const author = comment.authorName ?? t('history.systemUser');
    return comment.editedAt
      ? `${author} · ${written} · ${t('documents.comments.edited')}`
      : `${author} · ${written}`;
  };

  const body = (comment: DocumentComment) => (
    <div key={comment.id} style={{ marginBottom: 12 }}>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        {line(comment)}
      </Typography.Text>
      {editing === comment.id ? (
        <div style={{ marginTop: 4 }}>
          <Input.TextArea
            value={editDraft}
            onChange={(e) => setEditDraft(e.target.value)}
            rows={3}
            maxLength={MAX_BODY}
            showCount
            aria-label={t('documents.comments.body')}
          />
          <Space style={{ marginTop: 8 }}>
            <Button size="small" type="primary" onClick={() => void save(comment.id)}>
              {t('common.save')}
            </Button>
            <Button size="small" onClick={() => setEditing(null)}>
              {t('common.cancel')}
            </Button>
          </Space>
        </div>
      ) : (
        <>
          {/*
            The author's words, as words. pre-wrap keeps the line breaks they typed;
            overflow-wrap stops a pasted address from widening the page on a phone.
          */}
          <Typography.Paragraph
            style={{ marginTop: 2, marginBottom: 4, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}
          >
            {comment.body}
          </Typography.Paragraph>
          <Space size={4} wrap>
            {comment.parentId === null && (
              <Button
                size="small"
                type="text"
                onClick={() => {
                  setReplyTo(comment.id);
                  setReplyDraft('');
                }}
              >
                {t('documents.comments.reply')}
              </Button>
            )}
            {comment.mayEdit && (
              <Button
                size="small"
                type="text"
                icon={<EditOutlined />}
                onClick={() => {
                  setEditing(comment.id);
                  setEditDraft(comment.body);
                }}
              >
                {t('documents.comments.edit')}
              </Button>
            )}
            {comment.mayDelete && (
              <Popconfirm
                title={t('documents.comments.deleteConfirm')}
                okButtonProps={{ danger: true }}
                onConfirm={() => void drop(comment.id)}
              >
                <Button size="small" type="text" danger icon={<DeleteOutlined />}>
                  {t('documents.comments.delete')}
                </Button>
              </Popconfirm>
            )}
          </Space>
        </>
      )}
    </div>
  );

  return (
    <div>
      {roots.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('documents.comments.empty')} />
      ) : (
        roots.map((root) => (
          <div key={root.id}>
            {body(root)}
            {/* One level of replies, indented; less on a phone, where the width is the scarce thing. */}
            <div style={{ marginLeft: isMobile ? 12 : 32 }}>
              {repliesOf(root.id).map((reply) => body(reply))}
              {replyTo === root.id && (
                <div style={{ marginBottom: 12 }}>
                  <Input.TextArea
                    value={replyDraft}
                    onChange={(e) => setReplyDraft(e.target.value)}
                    rows={2}
                    maxLength={MAX_BODY}
                    showCount
                    placeholder={t('documents.comments.replyPlaceholder')}
                    aria-label={t('documents.comments.replyPlaceholder')}
                  />
                  <Space style={{ marginTop: 8 }}>
                    <Button
                      size="small"
                      type="primary"
                      onClick={() => void post(replyDraft, root.id)}
                      disabled={!replyDraft.trim()}
                    >
                      {t('documents.comments.post')}
                    </Button>
                    <Button size="small" onClick={() => setReplyTo(null)}>
                      {t('common.cancel')}
                    </Button>
                  </Space>
                </div>
              )}
            </div>
          </div>
        ))
      )}

      <Flex vertical gap={8} style={{ marginTop: 8 }}>
        <Input.TextArea
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          rows={3}
          maxLength={MAX_BODY}
          showCount
          placeholder={t('documents.comments.placeholder')}
          aria-label={t('documents.comments.placeholder')}
        />
        <Flex justify={isMobile ? 'stretch' : 'end'}>
          <Button
            type="primary"
            block={isMobile}
            loading={create.isPending}
            disabled={!draft.trim()}
            onClick={() => void post(draft, null)}
          >
            {t('documents.comments.post')}
          </Button>
        </Flex>
      </Flex>
    </div>
  );
}
