// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback } from 'react';
import { App } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * Asks whether to store content the archive already holds, answering true to store it anyway.
 *
 * The store refuses a second copy of bytes this caller can already see, and a person may well
 * want one anyway — a file that legitimately belongs to two objects, or one being attached
 * somewhere new. Without the question the refusal would surface as a bare failure. `known`
 * says whether the existing copy is one the caller can open, which changes what is worth saying.
 * Every place that uploads asks through this one prompt, so the question reads the same wherever
 * a file is dropped.
 */
export function useDuplicatePrompt(): (known: boolean) => Promise<boolean> {
  const { t } = useTranslation();
  const { modal } = App.useApp();
  return useCallback(
    (known: boolean) =>
      new Promise<boolean>((resolve) => {
        modal.confirm({
          title: t('uploads.duplicateTitle'),
          content: known ? t('uploads.duplicateKnown') : t('uploads.duplicateUnknown'),
          okText: t('uploads.duplicateStoreAnyway'),
          cancelText: t('uploads.duplicateSkip'),
          onOk: () => resolve(true),
          onCancel: () => resolve(false),
        });
      }),
    [modal, t],
  );
}
