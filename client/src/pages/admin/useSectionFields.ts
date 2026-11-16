// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import type { FormInstance } from 'antd';

/**
 * Puts one settings section's stored values into its form, and puts them back only when that
 * section's stored values change.
 *
 * Saving any section answers the whole stored settings document, so every section form on the
 * page receives a new settings object after every save. Keyed on the object, the forms of the
 * sections nobody saved would be reset too and lose whatever an administrator had typed into
 * them; keyed on the section's own values, only a change to that section — its own save, or the
 * stored values moving underneath — reaches its fields.
 *
 * `blank` names a secret field the server never sends back (a password, an authorisation
 * header): it is emptied whenever the stored values are put in, so an empty field keeps meaning
 * "leave the stored secret alone".
 */
export function useSectionFields(form: FormInstance, section: object, blank?: string) {
  // The settings document is plain JSON from the server, so its text is a faithful value key.
  const stored = JSON.stringify(section);
  useEffect(() => {
    const fields = JSON.parse(stored) as Record<string, unknown>;
    if (blank !== undefined) {
      fields[blank] = undefined;
    }
    form.setFieldsValue(fields);
  }, [form, stored, blank]);
}
