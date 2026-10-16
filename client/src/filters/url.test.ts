// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { decodeFilter, encodeFilter } from './url.ts';
import type { FilterDocument } from './types.ts';

/**
 * The token a link, a list request and an export all carry, pinned against the server.
 *
 * The generated contract sees a string, so nothing in either build would notice if the two sides
 * spelled a token differently. The same two literals appear in the server's `FilterTokenTests`,
 * asserted there to decode to these documents: change either side and exactly one test fails.
 */
const WIRE_DOCUMENT =
  '{"version":1,"scope":[{"world":"feature","where":{"node":"allOf","of":['
  + '{"node":"condition","field":"name","op":"contains","values":[{"type":"text","value":"urs"}]},'
  + '{"node":"condition","field":"createdAt","op":"greaterThan","values":'
  + '[{"type":"instant","value":"2026-01-01T00:00:00+00:00"}]}]}}],'
  + '"sort":"title","descending":false}';

const WIRE_TOKEN =
  'eyJ2ZXJzaW9uIjoxLCJzY29wZSI6W3sid29ybGQiOiJmZWF0dXJlIiwid2hlcmUiOnsibm9kZSI6ImFsbE9mIiwib2YiOlt7Im5vZGUiOiJjb25kaXRpb24iLCJmaWVsZCI6Im5hbWUiLCJvcCI6ImNvbnRhaW5zIiwidmFsdWVzIjpbeyJ0eXBlIjoidGV4dCIsInZhbHVlIjoidXJzIn1dfSx7Im5vZGUiOiJjb25kaXRpb24iLCJmaWVsZCI6ImNyZWF0ZWRBdCIsIm9wIjoiZ3JlYXRlclRoYW4iLCJ2YWx1ZXMiOlt7InR5cGUiOiJpbnN0YW50IiwidmFsdWUiOiIyMDI2LTAxLTAxVDAwOjAwOjAwKzAwOjAwIn1dfV19fV0sInNvcnQiOiJ0aXRsZSIsImRlc2NlbmRpbmciOmZhbHNlfQ';

/** A region with a Romanian letter: the browser encodes UTF-8 bytes, and the server must read them so. */
const ROMANIAN_DOCUMENT =
  '{"version":1,"scope":[{"world":"feature","where":{"node":"condition","field":"region","op":"contains",'
  + '"values":[{"type":"text","value":"Bihor ș"}]}}],"sort":"updated","descending":true}';

const ROMANIAN_TOKEN =
  'eyJ2ZXJzaW9uIjoxLCJzY29wZSI6W3sid29ybGQiOiJmZWF0dXJlIiwid2hlcmUiOnsibm9kZSI6ImNvbmRpdGlvbiIsImZpZWxkIjoicmVnaW9uIiwib3AiOiJjb250YWlucyIsInZhbHVlcyI6W3sidHlwZSI6InRleHQiLCJ2YWx1ZSI6IkJpaG9yIMiZIn1dfX1dLCJzb3J0IjoidXBkYXRlZCIsImRlc2NlbmRpbmciOnRydWV9';

describe('the filter token the server reads', () => {
  it('is what this side writes for the pinned document', () => {
    expect(encodeFilter(JSON.parse(WIRE_DOCUMENT) as FilterDocument)).toBe(WIRE_TOKEN);
  });

  it('carries text outside ascii as the server expects it', () => {
    expect(encodeFilter(JSON.parse(ROMANIAN_DOCUMENT) as FilterDocument)).toBe(ROMANIAN_TOKEN);
    expect(decodeFilter(ROMANIAN_TOKEN)).toEqual(JSON.parse(ROMANIAN_DOCUMENT));
  });
});
