// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ConfigProviderProps } from 'antd';
import type { TFunction } from 'i18next';

export type FormConfig = NonNullable<ConfigProviderProps['form']>;

// Every value type antd's rule checker names. They share one sentence: the type's own name is an
// English word ("email", "url") that would otherwise be pasted into the Romanian text.
const VALUE_TYPES = [
  'string',
  'method',
  'array',
  'object',
  'number',
  'date',
  'boolean',
  'integer',
  'float',
  'regexp',
  'email',
  'url',
  'hex',
] as const;

/**
 * The settings every form in the application shares, handed to antd's ConfigProvider.
 *
 * A refused save scrolls to the first field that needs attention and puts the cursor in it: on a
 * long form the button sits several screens below the field that failed, and without this the
 * press looked as if it had done nothing.
 *
 * The messages replace antd's stock ones, which paste the field's label into a fixed sentence —
 * "Please enter Repeat password", and in Romanian the formal "Vă rugăm introduceți …" in an
 * application that otherwise speaks to its reader familiarly. The `${label}` placeholders are
 * antd's own and are filled in by the form, not by the translation library.
 */
export function buildFormConfig(t: TFunction): FormConfig {
  const typeMessage = t('common.validate.type');
  return {
    scrollToFirstError: { block: 'center', focus: true },
    validateMessages: {
      default: t('common.validate.default'),
      required: t('common.validate.required'),
      whitespace: t('common.validate.whitespace'),
      enum: t('common.validate.enum'),
      types: Object.fromEntries(VALUE_TYPES.map((type) => [type, typeMessage])),
      string: {
        len: t('common.validate.stringLen'),
        min: t('common.validate.stringMin'),
        max: t('common.validate.stringMax'),
        range: t('common.validate.stringRange'),
      },
      number: {
        len: t('common.validate.numberLen'),
        min: t('common.validate.numberMin'),
        max: t('common.validate.numberMax'),
        range: t('common.validate.numberRange'),
      },
      array: {
        len: t('common.validate.arrayLen'),
        min: t('common.validate.arrayMin'),
        max: t('common.validate.arrayMax'),
        range: t('common.validate.arrayRange'),
      },
      pattern: { mismatch: t('common.validate.pattern') },
    },
  };
}
