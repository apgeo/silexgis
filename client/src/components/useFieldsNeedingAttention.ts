// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useState } from 'react';
import type { FormInstance } from 'antd';

type ErrorField = { name: (string | number)[]; errors: string[] };

/**
 * Counts the fields a refused save left in error, for the line beside a long form's Save button.
 *
 * On a long form the field that failed is screens above the button, so a press that is refused
 * looks as if it did nothing. The count is taken when a save is refused and follows the reader's
 * corrections from then on, so the line goes away once the last field is fixed.
 */
export function useFieldsNeedingAttention(form: FormInstance) {
  const [count, setCount] = useState(0);

  const countErrors = useCallback(
    () => form.getFieldsError().filter((field) => field.errors.length > 0).length,
    [form],
  );

  return {
    count,
    /** For a Form's `onFinishFailed`, where the form itself has already scrolled to the field. */
    onFinishFailed: useCallback((info: { errorFields: ErrorField[] }) => {
      setCount(info.errorFields.length);
    }, []),
    /**
     * For a save that validates by hand (a dialog's OK). Nothing scrolls there on its own, so this
     * takes the reader to the first failed field as a submitted form would.
     */
    onValidateRefused: useCallback(
      (refusal: unknown) => {
        const fields = (refusal as { errorFields?: ErrorField[] } | null)?.errorFields ?? [];
        setCount(fields.length);
        if (fields.length > 0) {
          form.scrollToField(fields[0].name, { block: 'center', focus: true });
        }
      },
      [form],
    ),
    /** For a Form's `onFieldsChange`: recounts once a refusal has been shown, and only then. */
    onFieldsChange: useCallback(() => {
      setCount((shown) => (shown > 0 ? countErrors() : 0));
    }, [countErrors]),
  };
}
