// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useRef } from 'react';
import { App } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * The mask setting every long form's Modal or Drawer takes: a click beside the dialog never
 * closes it. Escape, the close cross and Cancel still reach the dialog's cancel handler, which
 * routes through {@link useDiscardGuard} so they ask first when something was typed.
 */
export const KEEP_OPEN_ON_OUTSIDE_CLICK = { closable: false } as const;

/**
 * Whether the form holds changes the user made: a boolean for state the caller already tracks,
 * or a function read at the moment of closing.
 */
export type DirtySource = boolean | (() => boolean);

/**
 * Records whether the user edited an antd form. Hand `onValuesChange` to the `<Form>` and read
 * `edited` when closing. antd's own `form.isFieldsTouched()` cannot serve: it also counts a field
 * as touched when `setFieldsValue` changed it, so every edit form filled from a saved record
 * would ask on a Cancel nobody typed before. `onValuesChange` fires for the user's own edits only.
 * `forget` is for after a save that leaves the form open.
 *
 * Pass the dialog's `open` when the dialog stays mounted between openings: each opening then
 * starts with nothing typed, whatever was typed and thrown away the time before.
 */
export function useFormEdits(open?: boolean): { onValuesChange: () => void; edited: () => boolean; forget: () => void } {
  const edited = useRef(false);
  useEffect(() => {
    if (open) {
      edited.current = false;
    }
  }, [open]);
  return useMemo(
    () => ({
      onValuesChange: () => {
        edited.current = true;
      },
      edited: () => edited.current,
      forget: () => {
        edited.current = false;
      },
    }),
    [],
  );
}

/**
 * Returns `requestDiscard(dirty, discard)`: closes at once when nothing was typed, otherwise asks
 * "Discard your changes?" and closes only on the user's yes. Use it for the user's own way out
 * (Cancel, Escape, the close cross) and never for the close a successful save performs.
 *
 * One question at a time: a second Escape while the question is showing does not stack another.
 */
export function useDiscardGuard(): (dirty: DirtySource, discard: () => void) => void {
  const { t } = useTranslation();
  const { modal } = App.useApp();
  const asking = useRef(false);

  return useCallback(
    (dirty: DirtySource, discard: () => void) => {
      const changed = typeof dirty === 'function' ? dirty() : dirty;
      if (!changed) {
        discard();
        return;
      }
      if (asking.current) {
        return;
      }
      asking.current = true;
      modal.confirm({
        title: t('common.discardTitle'),
        content: t('common.discardBody'),
        okText: t('common.discardOk'),
        okButtonProps: { danger: true, 'data-testid': 'discard-confirm' },
        cancelText: t('common.keepEditing'),
        onOk: () => {
          asking.current = false;
          discard();
        },
        onCancel: () => {
          asking.current = false;
        },
      });
    },
    [modal, t],
  );
}
