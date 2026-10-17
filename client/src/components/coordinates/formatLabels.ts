// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import type { FormatId } from '../../geo/formats/registry.ts';

/** A format's name as a person reads it. */
export function formatLabel(t: TFunction, id: FormatId): string {
  return t(`coordinates.formats.${id}`);
}

/** The name of one of a format's settings. */
export function settingLabel(t: TFunction, key: string): string {
  return t(`coordinates.settingLabels.${key}`, { defaultValue: key });
}

/**
 * One choice of a format's setting, as a person reads it. The settings are the same few kinds
 * across formats (places, a precision in metres, a zone, a code length), so the wording is chosen
 * by the setting's name rather than by the format.
 */
export function choiceLabel(t: TFunction, key: string, value: string | number): string {
  switch (key) {
    case 'style':
    case 'spacing':
    case 'axes':
    case 'length':
      return t(`coordinates.choices.${key}.${value}`, { defaultValue: String(value) });
    case 'zone':
      return value === 'auto'
        ? t('coordinates.choices.zone.auto')
        : t('coordinates.choices.zone.forced', { zone: value });
    case 'precisionMetres':
      return typeof value === 'number' && value >= 1000
        ? t('coordinates.choices.kilometres', { value: value / 1000 })
        : t('coordinates.choices.metres', { value });
    default:
      return String(value);
  }
}

/** The error a text that could not be read as a position is explained by. */
export function parseErrorLabel(t: TFunction, error: string): string {
  return t(`coordinates.errors.${error}`, { defaultValue: t('coordinates.errors.unrecognised') });
}

/** What was assumed in reading a position, as a sentence. */
export function assumptionLabel(t: TFunction, assumption: string): string {
  return t(`coordinates.assumptions.${assumption}`);
}
