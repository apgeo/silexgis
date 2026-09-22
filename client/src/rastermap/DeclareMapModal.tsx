// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, App, Button, Flex, Modal, Popconfirm, Segmented, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateResLink,
  useDeleteResLink,
  useDocument,
  useFile,
  useResLinkRelationTypes,
  useUpdateResLink,
} from '../api/hooks.ts';
import { resLinkProblemMessage } from '../components/reslinks/problems.ts';
import ResLinkTargetPicker from '../components/reslinks/ResLinkTargetPicker.tsx';
import { mapDeclarationBody } from './authoring.ts';
import type { RasterMapDeclaration } from './rasterMaps.ts';
import { codeOfViewKind, MAP_VIEW_KINDS, type MapViewKind } from './vocabulary.ts';

interface Props {
  open: boolean;
  onClose: () => void;
  surveyModelId: string;
  /**
   * The declaration being edited, or null to declare a new map. Editing offers the two
   * amendments a declaration admits — its view kind (a PATCH swapping the relation code)
   * and its removal (deleting the link) — and never re-asks which document, because a
   * different document is a different map, not this one edited.
   */
  editing?: RasterMapDeclaration | null;
}

/**
 * Declaring a scanned map of a survey model, and amending a declaration.
 *
 * <b>One POST, same as authoring it through the generic link dialog.</b> A declaration is
 * an ordinary resource link — document (main, whole) onto model (whole) under a seeded
 * map-of code — so this dialog is a thin, purpose-shaped face over the same create
 * mutation the generic flow uses. It exists instead of presetting the generic dialog
 * because the relation must read out of the *document* (edit rights follow the map, and
 * the tab reads "plan map of"), while every preset-relation flow makes its origin the
 * main member — and the origin here is the model the viewer is standing in.
 *
 * <b>The image check is a UI affordance, not a rule.</b> The server's link rules are
 * deliberately generic and admit any document; a map tab for a PDF would merely show the
 * missing-image state. Refusing it here, where the person is looking at a picker, is
 * kindness — done with the same entitlement-shaped facts the tab itself will use.
 */
export default function DeclareMapModal({ open, onClose, surveyModelId, editing = null }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const createLink = useCreateResLink();
  const updateLink = useUpdateResLink();
  const deleteLink = useDeleteResLink();

  const [documentId, setDocumentId] = useState<string | null>(null);
  const [viewKind, setViewKind] = useState<MapViewKind>('plan');

  // Every open starts from what it is about: a fresh declaration from nothing, an edit
  // from the declaration's own current kind.
  useEffect(() => {
    if (open) {
      setDocumentId(null);
      setViewKind(editing?.viewKind ?? 'plan');
    }
  }, [open, editing]);

  const { data: relationTypes } = useResLinkRelationTypes(open);
  const relationIdOf = (kind: MapViewKind): number | null =>
    relationTypes?.find((row) => row.code === codeOfViewKind(kind))?.id ?? null;

  // The picked document's current file decides whether this can be a map at all.
  const pickedId = editing === null ? documentId : null;
  const { data: pickedDocument } = useDocument(pickedId ?? undefined);
  const { data: pickedFile } = useFile(pickedDocument?.currentFileId ?? undefined);
  const notAnImage = pickedFile !== undefined && !pickedFile.mimeType.startsWith('image/');

  const relationId = relationIdOf(viewKind);
  const canSubmit =
    relationId !== null
    && (editing !== null
      ? viewKind !== editing.viewKind
      : documentId !== null && pickedFile !== undefined && !notAnImage);

  const submit = async () => {
    if (!canSubmit || relationId === null) {
      return;
    }
    try {
      if (editing === null) {
        await createLink.mutateAsync(mapDeclarationBody(relationId, documentId!, surveyModelId));
        message.success(t('rastermap.mapDeclared'));
      } else {
        // Only the relation changes; the description rides back unchanged, and no
        // mainMemberId means the main marker stays where it is — on the document.
        await updateLink.mutateAsync({
          id: editing.linkId,
          body: { description: editing.description, relationTypeId: relationId, mainMemberId: null },
        });
        message.success(t('common.saved'));
      }
      onClose();
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  const undeclare = async () => {
    if (editing === null) {
      return;
    }
    try {
      await deleteLink.mutateAsync(editing.linkId);
      message.success(t('rastermap.mapRemoved'));
      onClose();
    } catch (error) {
      message.error(resLinkProblemMessage(error, t));
    }
  };

  return (
    <Modal
      open={open}
      title={
        editing === null
          ? t('rastermap.declareTitle')
          : t('rastermap.editMapTitle', { title: editing.title ?? t('rastermap.untitledMap') })
      }
      onCancel={onClose}
      onOk={() => void submit()}
      okButtonProps={{
        disabled: !canSubmit,
        loading: createLink.isPending || updateLink.isPending,
        'data-testid': 'rastermap-declare-submit',
      }}
      width={480}
      destroyOnHidden
    >
      <Flex vertical gap={12}>
        {editing === null && (
          <Flex vertical gap={4}>
            <Typography.Text strong>{t('rastermap.mapDocument')}</Typography.Text>
            <ResLinkTargetPicker
              targetType="document"
              value={documentId}
              onChange={(id) => setDocumentId(id)}
            />
            {notAnImage && (
              <Alert
                type="warning"
                showIcon
                title={t('rastermap.notAnImage')}
                data-testid="rastermap-not-an-image"
              />
            )}
          </Flex>
        )}

        <Flex vertical gap={4}>
          <Typography.Text strong>{t('rastermap.viewKind')}</Typography.Text>
          <Segmented<MapViewKind>
            value={viewKind}
            onChange={setViewKind}
            options={MAP_VIEW_KINDS.map((kind) => ({
              value: kind,
              label: t(`rastermap.viewKinds.${kind}`),
            }))}
            data-testid="rastermap-view-kind"
          />
        </Flex>

        {editing !== null && (
          <Flex vertical gap={4}>
            {/* Removal deletes the declaration link alone: the tab goes, the document
                stays, and the pin links stay (undisplayed until a map shows them again) —
                the cascade the design refuses to have. */}
            <Popconfirm
              title={t('rastermap.undeclareConfirm')}
              onConfirm={() => void undeclare()}
              okButtonProps={{ danger: true, loading: deleteLink.isPending }}
            >
              <Button danger data-testid="rastermap-undeclare">
                {t('rastermap.undeclare')}
              </Button>
            </Popconfirm>
            <Typography.Text type="secondary">{t('rastermap.undeclareHint')}</Typography.Text>
          </Flex>
        )}
      </Flex>
    </Modal>
  );
}
