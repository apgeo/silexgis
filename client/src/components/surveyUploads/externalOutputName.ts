// SPDX-License-Identifier: AGPL-3.0-or-later
/**
 * The name a product is sent back under: of the names its step takes it under, the one with the
 * chosen file's extension (a cloud may come back as LAZ or LAS), else the first. The file's own
 * name does not matter — the program that made it may have called it anything.
 */
export function externalOutputName(names: readonly string[], fileName: string): string {
  const dot = fileName.lastIndexOf('.');
  const extension = dot < 0 ? '' : fileName.slice(dot).toLowerCase();
  return names.find((name) => extension !== '' && name.toLowerCase().endsWith(extension)) ?? names[0] ?? fileName;
}
