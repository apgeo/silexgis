// SPDX-License-Identifier: AGPL-3.0-or-later

// Kinds that carry typed data — feature types and document types alike — publish an
// optional JSON schema describing it, and the values live in a jsonb bag beside it.
// Reading such a schema into form fields is one piece of knowledge, so it lives here
// rather than once per kind of thing that has one.
//
// Only a minimal, flat subset is supported: an object schema whose properties are
// string / number / integer / boolean or a string enum. Anything unrecognized is skipped
// — the server stores the property document verbatim either way, so a value this cannot
// render is preserved rather than lost.

export interface SchemaField {
  key: string;
  label: string;
  kind: 'string' | 'number' | 'integer' | 'boolean' | 'enum';
  required: boolean;
  /**
   * Declared <code>readOnly</code>: a value some other system writes — a catalogue code, a device's
   * index — that a form shows nowhere for editing. The server stores the document verbatim either
   * way; this only keeps a form from offering a box for it.
   */
  readOnly: boolean;
  enumValues?: string[];
  min?: number;
  max?: number;
}

export function parsePropertiesSchema(schemaJson: string | null | undefined): SchemaField[] {
  if (!schemaJson) {
    return [];
  }

  let schema: unknown;
  try {
    schema = JSON.parse(schemaJson);
  } catch {
    return [];
  }

  if (!isRecord(schema) || schema.type !== 'object' || !isRecord(schema.properties)) {
    return [];
  }

  const required = new Set(
    Array.isArray(schema.required) ? schema.required.filter((k): k is string => typeof k === 'string') : [],
  );

  const fields: SchemaField[] = [];
  for (const [key, spec] of Object.entries(schema.properties)) {
    if (!isRecord(spec)) {
      continue;
    }

    const label = typeof spec.title === 'string' && spec.title ? spec.title : key;
    const enumValues = Array.isArray(spec.enum)
      ? spec.enum.filter((v): v is string => typeof v === 'string')
      : undefined;

    let kind: SchemaField['kind'] | undefined;
    if (enumValues && enumValues.length > 0) {
      kind = 'enum';
    } else if (spec.type === 'string') {
      kind = 'string';
    } else if (spec.type === 'number') {
      kind = 'number';
    } else if (spec.type === 'integer') {
      kind = 'integer';
    } else if (spec.type === 'boolean') {
      kind = 'boolean';
    }

    if (!kind) {
      continue; // nested objects/arrays and unknown types are not rendered
    }

    fields.push({
      key,
      label,
      kind,
      required: required.has(key),
      readOnly: spec.readOnly === true,
      enumValues: kind === 'enum' ? enumValues : undefined,
      min: typeof spec.minimum === 'number' ? spec.minimum : undefined,
      max: typeof spec.maximum === 'number' ? spec.maximum : undefined,
    });
  }

  return fields;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
