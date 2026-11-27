// SPDX-License-Identifier: AGPL-3.0-or-later
import { createElement, Fragment, useCallback } from 'react';
import { App, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useReadDocumentFiling } from '../../api/hooks.ts';

/** What the store refused: the file this person dropped, and the copy it already holds. */
export interface DuplicateQuestion {
  /** The name of the file being uploaded — several may have been dropped at once. */
  fileName: string;
  /** The document already holding these contents, when it is one this person can open. */
  documentId?: string;
}

/**
 * Asks whether to store content the archive already holds, answering true to store it anyway.
 *
 * The store refuses a second copy of bytes this caller can already see, and a person may well
 * want one anyway — a file that legitimately belongs to two objects, or one being attached
 * somewhere new. Without the question the refusal would surface as a bare failure. Every place
 * that uploads asks through this one prompt, so the question reads the same wherever a file is
 * dropped.
 *
 * <b>It names both files.</b> A drop of three files that stops on "this file is already here"
 * leaves the person guessing which of the three, and "here" is wrong when the copy is filed in
 * another cabinet. So the question names the file dropped and, when the existing document is one
 * this person can open, its title and cabinets with a link that opens it in a new tab (the upload
 * waits on the answer, so leaving this page would abandon it). A copy this person cannot open is
 * not described at all: the server named no document, and saying where it is would be a leak.
 */
export function useDuplicatePrompt(): (question: DuplicateQuestion) => Promise<boolean> {
  const { t } = useTranslation();
  const { modal } = App.useApp();
  const readFiling = useReadDocumentFiling();
  return useCallback(
    async ({ fileName, documentId }: DuplicateQuestion) => {
      // Read before the dialog opens: its content is drawn outside this component's providers,
      // so it can be given words and not queries. A failed read still asks, without the filing.
      const filing =
        documentId === undefined
          ? null
          : await readFiling(documentId).catch(() => null);
      const where =
        filing === null
          ? null
          : createElement(
              Typography.Paragraph,
              null,
              createElement(
                Typography.Link,
                { href: `/documents/${documentId}`, target: '_blank', rel: 'noopener' },
                filing.title,
              ),
              ' — ',
              filing.cabinets.length > 0
                ? t('uploads.duplicateFiledIn', { cabinets: filing.cabinets.join(', ') })
                : t('uploads.duplicateUnfiled'),
            );
      return new Promise<boolean>((resolve) => {
        modal.confirm({
          title: t('uploads.duplicateTitle'),
          content: createElement(
            Fragment,
            null,
            createElement(
              Typography.Paragraph,
              null,
              documentId === undefined
                ? t('uploads.duplicateUnknown', { file: fileName })
                : t('uploads.duplicateKnown', { file: fileName }),
            ),
            where,
          ),
          okText: t('uploads.duplicateStoreAnyway'),
          cancelText: t('uploads.duplicateSkip'),
          onOk: () => resolve(true),
          onCancel: () => resolve(false),
        });
      });
    },
    [modal, readFiling, t],
  );
}
