// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { App } from 'antd';
import { useTranslation } from 'react-i18next';
import { useBlocker } from 'react-router-dom';
import { isLeavingDeliberately } from '../../auth/leaving.ts';

/**
 * Asks before the map page is left while it holds unsaved edits. Drawings and reshapes live only
 * in the page's editor until they are saved, and leaving the page disposes of the editor, so a
 * link followed from the selection panel, the main menu, the back button, a reload or a closed
 * tab would otherwise drop them without a word.
 *
 * Moves that stay on the map page (a changed query string, say) are not asked about: the editor
 * survives them. A reload or a closed tab can only be met by the browser's own question, whose
 * wording the browser chooses.
 */
export function useUnsavedEditsGuard(unsaved: number): void {
  const { t } = useTranslation();
  const { modal } = App.useApp();

  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      unsaved > 0 && currentLocation.pathname !== nextLocation.pathname,
  );

  useEffect(() => {
    if (blocker.state !== 'blocked') {
      return;
    }
    const dialog = modal.confirm({
      title: t('mapEdit.leaveTitle'),
      content: t('mapEdit.leaveBody', { count: unsaved }),
      okText: t('mapEdit.leaveOk'),
      okButtonProps: { danger: true },
      cancelText: t('mapEdit.leaveStay'),
      onOk: () => blocker.proceed(),
      onCancel: () => blocker.reset(),
    });
    return () => dialog.destroy();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- asked once per blocked move
  }, [blocker.state]);

  useEffect(() => {
    if (unsaved === 0) {
      return;
    }
    const ask = (event: BeforeUnloadEvent) => {
      if (isLeavingDeliberately()) {
        return;
      }
      event.preventDefault();
      // Older engines show their question only when a return value is set.
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', ask);
    return () => window.removeEventListener('beforeunload', ask);
  }, [unsaved]);
}
