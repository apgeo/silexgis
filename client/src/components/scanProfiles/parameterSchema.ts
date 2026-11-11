// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Reading a processing step's parameter schema into the fields of a form.
 *
 * The schema is JSON Schema as the server answers it: an object whose properties are either
 * settings or objects of settings. HDMapping's odometry nests its settings two levels down —
 * `{values: {section: {key: value}}}`, one object per TOML section — and the tiler's are flat; one
 * walk covers both, because a group is simply "an object that holds settings", titled by its own
 * key. Nothing here knows any step's keys: the form follows whatever version of a tool the server
 * says is installed.
 *
 * Kept free of React so the rules the editor depends on — what a field's default is, when a value
 * counts as changed, how a value is written back without touching its neighbours — are tested on
 * their own.
 */

export type ParameterKind = 'boolean' | 'integer' | 'number' | 'choice' | 'fixed' | 'text' | 'numberList' | 'textList';

export interface ParameterField {
  /** Where the value sits in the parameter object, outermost key first. */
  path: readonly string[];
  /** The key as the tool names it; shown as written, since it is the tool's word, not ours. */
  key: string;
  kind: ParameterKind;
  description?: string;
  minimum?: number;
  maximum?: number;
  /** A bound the value may not reach, rather than may not pass. */
  exclusiveMinimum?: number;
  exclusiveMaximum?: number;
  /** The values a `choice` may take. */
  options?: readonly string[];
  /** What the step runs with when a profile says nothing; undefined when the schema has none. */
  defaultValue: unknown;
}

export interface ParameterGroup {
  /** The path of the object holding these settings, joined with dots; empty for the top level. */
  id: string;
  /** The object's own key (a TOML section's name, for HDMapping); empty for the top level. */
  title: string;
  fields: ParameterField[];
}

type SchemaNode = Record<string, unknown>;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function typeOf(node: SchemaNode): string | undefined {
  const type = node.type;
  if (typeof type === 'string') return type;
  // A nullable setting is written as ["number", "null"]; the form edits the non-null kind.
  if (Array.isArray(type)) return type.find((t): t is string => typeof t === 'string' && t !== 'null');
  return undefined;
}

function numberOr(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined;
}

/** The value at a path, or undefined when any step of the way is missing. */
export function valueAt(source: unknown, path: readonly string[]): unknown {
  let here: unknown = source;
  for (const key of path) {
    if (!isRecord(here) || !Object.hasOwn(here, key)) return undefined;
    here = here[key];
  }
  return here;
}

/**
 * A copy of `source` with `value` at `path`, every object on the way copied and every neighbour
 * kept as it was. Objects missing on the way are made, so a partial profile can gain a key in a
 * section it did not name.
 */
export function withValueAt(source: unknown, path: readonly string[], value: unknown): Record<string, unknown> {
  const root: Record<string, unknown> = isRecord(source) ? { ...source } : {};
  let here = root;
  path.forEach((key, index) => {
    if (index === path.length - 1) {
      here[key] = value;
      return;
    }
    const next = isRecord(here[key]) ? { ...(here[key] as Record<string, unknown>) } : {};
    here[key] = next;
    here = next;
  });
  return root;
}

/** Structural equality over JSON values: what "the same as the default" means for a setting. */
export function sameValue(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (Array.isArray(a) && Array.isArray(b)) {
    return a.length === b.length && a.every((item, i) => sameValue(item, b[i]));
  }
  if (isRecord(a) && isRecord(b)) {
    const keys = Object.keys(a);
    return keys.length === Object.keys(b).length && keys.every((k) => Object.hasOwn(b, k) && sameValue(a[k], b[k]));
  }
  return false;
}

function fieldOf(node: SchemaNode, path: readonly string[], defaults: unknown): ParameterField {
  const key = path[path.length - 1];
  const type = typeOf(node);
  const fromDefaults = valueAt(defaults, path);
  const field: ParameterField = {
    path,
    key,
    kind: 'text',
    description: typeof node.description === 'string' ? node.description : undefined,
    minimum: numberOr(node.minimum),
    maximum: numberOr(node.maximum),
    exclusiveMinimum: numberOr(node.exclusiveMinimum),
    exclusiveMaximum: numberOr(node.exclusiveMaximum),
    defaultValue: fromDefaults !== undefined ? fromDefaults : node.default,
  };
  if (Object.hasOwn(node, 'const')) {
    // A setting the tool keeps for itself (HDMapping's text settings): shown, never edited.
    return { ...field, kind: 'fixed', defaultValue: field.defaultValue ?? node.const };
  }
  if (Array.isArray(node.enum) && node.enum.every((v) => typeof v === 'string')) {
    return { ...field, kind: 'choice', options: node.enum as string[] };
  }
  switch (type) {
    case 'boolean':
      return { ...field, kind: 'boolean' };
    case 'integer':
      return { ...field, kind: 'integer' };
    case 'number':
      return { ...field, kind: 'number' };
    case 'array': {
      const items = isRecord(node.items) ? typeOf(node.items) : undefined;
      return { ...field, kind: items === 'string' ? 'textList' : 'numberList' };
    }
    default:
      return field;
  }
}

/**
 * The groups of a step's form, in the schema's own order: the top level's settings first (when it
 * has any), then each object of settings, depth first. An object with no settings of its own and
 * only objects inside (HDMapping's `values` wrapper) makes no group of its own.
 */
export function parameterGroups(schema: unknown, defaults: unknown): ParameterGroup[] {
  const groups: ParameterGroup[] = [];
  const walk = (node: unknown, path: readonly string[]) => {
    if (!isRecord(node) || !isRecord(node.properties)) return;
    const fields: ParameterField[] = [];
    const nested: [string, SchemaNode][] = [];
    for (const [key, child] of Object.entries(node.properties)) {
      if (!isRecord(child)) continue;
      if (typeOf(child) === 'object' || isRecord(child.properties)) nested.push([key, child]);
      else fields.push(fieldOf(child, [...path, key], defaults));
    }
    if (fields.length > 0) {
      groups.push({ id: path.join('.'), title: path[path.length - 1] ?? '', fields });
    }
    for (const [key, child] of nested) walk(child, [...path, key]);
  };
  walk(schema, []);
  return groups;
}

/** The value a field shows: the profile's own, or the default when the profile does not name it. */
export function fieldValue(field: ParameterField, values: unknown): unknown {
  const own = valueAt(values, field.path);
  return own === undefined ? field.defaultValue : own;
}

/** Whether the profile sets this field to something other than its default. */
export function fieldDiffers(field: ParameterField, values: unknown): boolean {
  const own = valueAt(values, field.path);
  return own !== undefined && !sameValue(own, field.defaultValue);
}

/** Every field the profile changed from its default, in form order. */
export function changedFields(groups: readonly ParameterGroup[], values: unknown): ParameterField[] {
  return groups.flatMap((group) => group.fields.filter((field) => fieldDiffers(field, values)));
}

/** A list setting as the person types it: the items separated by commas. */
export function listText(value: unknown): string {
  return Array.isArray(value) ? value.map((item) => String(item)).join(', ') : '';
}

/**
 * The list a person typed, or null when a number list holds something that is not a number — the
 * form keeps what was typed and says so rather than quietly saving a shorter list.
 */
export function parseList(text: string, kind: 'numberList' | 'textList'): unknown[] | null {
  const items = text
    .split(',')
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
  if (kind === 'textList') return items;
  const numbers = items.map(Number);
  return numbers.every(Number.isFinite) ? numbers : null;
}
