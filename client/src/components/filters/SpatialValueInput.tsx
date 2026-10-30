// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Flex, Input, InputNumber, Select, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { documentOf, useFilterVocabulary, useSavedFilters } from '../../api/hooks.ts';
import {
  ANCHOR_WORLD,
  anchorScopes,
  DEFAULT_MAX_RADIUS_METERS,
  isOnGlobe,
  isRadius,
  MIN_RADIUS_METERS,
  parsePolygon,
  polygonText,
  type InsideOutlineKind,
  type PolygonProblem,
  type WithinAnchorKind,
} from '../../filters/spatial.ts';
import type { FilterLimits, FilterOp, FilterValue } from '../../filters/types.ts';
import { useSpatialSources } from '../../filters/spatialSources.ts';
import Button from '../Button.tsx';
import ObjectSelector from '../selector/ObjectSelector.tsx';

/**
 * The value part of a spatial row: a radius and what it is measured from, or an outline.
 *
 * Nothing typed here is a position the reader may not already know — a point they typed, a shape
 * they pasted — and anything picked is only named: an object or a saved filter is looked up by the
 * server under the reader's own access every time the filter runs. What the server will not let
 * them measure from (an object they may not place exactly, a filter they cannot read) is refused
 * there in one sentence, shown on this row; nothing here tries to guess it first.
 *
 * Unfinished values are held the way the other inputs hold them: a radius not typed is an empty
 * number in its place and an anchor not chosen is absent, so the row is left out of what is counted
 * until it is whole.
 */

export interface SpatialValueInputProps {
  /**
   * The world the row filters. What it measures from is a feature whatever the world — the one
   * kind of row whose position the server's placement gate decides.
   */
  world: string;
  op: FilterOp;
  values: FilterValue[];
  onChange: (values: FilterValue[]) => void;
  testId: string;
}

const BLANK_RADIUS = { type: 'number', value: null } as unknown as FilterValue;

export default function SpatialValueInput(props: SpatialValueInputProps) {
  if (props.op === 'within') {
    return <WithinInput {...props} />;
  }
  if (props.op === 'inside') {
    return <InsideInput {...props} />;
  }
  return null;
}

/** The installation's radius ceiling, as the server publishes it; the shipped one until it arrives. */
function useMaxRadius(): number {
  const { data } = useFilterVocabulary();
  const limits = data?.limits as FilterLimits | undefined;
  return limits?.maxRadiusMeters ?? DEFAULT_MAX_RADIUS_METERS;
}

function WithinInput({ values, onChange, testId }: SpatialValueInputProps) {
  const { t } = useTranslation();
  const maxRadius = useMaxRadius();
  const radiusValue = values[0]?.type === 'number' && typeof values[0].value === 'number' ? values[0].value : null;
  const anchor = values[1];
  const sources = useSpatialSources();
  const selectedId = sources?.selectedFeatureId ?? null;
  const [kind, setKind] = useState<WithinAnchorKind>(
    anchor?.type === 'point' || anchor?.type === 'savedFilter'
      ? anchor.type
      : anchor?.type === 'object' && selectedId !== null && anchor.id === selectedId
        ? 'selection'
        : 'object');
  const anchorKinds: WithinAnchorKind[] = sources === null
    ? ['object', 'point', 'savedFilter']
    : ['selection', 'object', 'point', 'savedFilter'];

  const put = (radius: number | null, next: FilterValue | undefined) => {
    const r: FilterValue = radius === null ? BLANK_RADIUS : { type: 'number', value: radius };
    onChange(radius === null && next === undefined ? [] : next === undefined ? [r] : [r, next]);
  };

  return (
    <Flex gap={8} wrap align="center">
      <InputNumber
        aria-label={t('filters.editor.spatial.radius')}
        data-testid={`${testId}-radius`}
        min={MIN_RADIUS_METERS}
        max={maxRadius}
        value={radiusValue}
        suffix={t('filters.editor.spatial.metres')}
        status={radiusValue !== null && !isRadius(radiusValue, maxRadius) ? 'error' : undefined}
        onChange={(n) => put(typeof n === 'number' ? n : null, anchor)}
        style={{ width: 140 }}
      />
      <Typography.Text>{t('filters.editor.spatial.of')}</Typography.Text>
      <Select
        aria-label={t('filters.editor.spatial.anchorKind')}
        data-testid={`${testId}-anchor-kind`}
        value={kind}
        onChange={(next: WithinAnchorKind) => {
          setKind(next);
          // The selected object is already known, so choosing it finishes the anchor.
          put(radiusValue, next === 'selection' && selectedId !== null
            ? { type: 'object', world: ANCHOR_WORLD, id: selectedId }
            : undefined);
        }}
        options={anchorKinds.map((k) => ({
          value: k,
          label: t(`filters.editor.spatial.anchorKinds.${k}`),
          // Nothing selected on the map: there is nothing to measure from yet.
          disabled: k === 'selection' && selectedId === null,
        }))}
        style={{ width: 170 }}
      />
      {kind === 'selection' && (
        <Typography.Text type={selectedId === null ? 'secondary' : undefined} data-testid={`${testId}-selection`}>
          {t(selectedId === null ? 'filters.editor.spatial.noSelection' : 'filters.editor.spatial.selectionNamed')}
        </Typography.Text>
      )}
      {kind === 'point' && (
        <PointInput
          value={anchor?.type === 'point' ? anchor : null}
          onChange={(p) => put(radiusValue, p === null ? undefined : { type: 'point', ...p })}
          testId={testId}
        />
      )}
      {kind === 'object' && (
        <ObjectSelector
          scopes={anchorScopes('object')}
          presentation="dropdown"
          sorts={[]}
          value={anchor?.type === 'object' ? anchor.id : null}
          onChange={(id) => put(radiusValue, typeof id === 'string' ? { type: 'object', world: ANCHOR_WORLD, id } : undefined)}
          aria-label={t('filters.editor.spatial.object')}
          data-testid={`${testId}-object`}
        />
      )}
      {kind === 'savedFilter' && (
        <SavedFilterPicker
          world={ANCHOR_WORLD}
          value={anchor?.type === 'savedFilter' ? anchor.id : null}
          onChange={(id) => put(radiusValue, id === null ? undefined : { type: 'savedFilter', id })}
          testId={`${testId}-saved`}
        />
      )}
    </Flex>
  );
}

function InsideInput({ values, onChange, testId }: SpatialValueInputProps) {
  const { t } = useTranslation();
  const outline = values[0];
  const sources = useSpatialSources();
  const [kind, setKind] = useState<InsideOutlineKind>(outline?.type === 'polygon' ? 'polygon' : 'area');
  const [drawing, setDrawing] = useState(false);
  const outlineKinds: InsideOutlineKind[] = sources === null
    ? ['area', 'polygon']
    : ['view', 'draw', 'area', 'polygon'];

  const takeView = () => {
    const rings = sources?.currentView() ?? null;
    onChange(rings === null ? [] : [{ type: 'polygon', coordinates: rings }]);
  };
  const draw = () => {
    if (sources === null) {
      return;
    }
    setDrawing(true);
    // A drawing given up leaves the row as it was: the person may have meant to keep the old one.
    void sources.drawOutline().then((rings) => {
      if (rings !== null) {
        onChange([{ type: 'polygon', coordinates: rings }]);
      }
    }).finally(() => setDrawing(false));
  };
  const corners = outline?.type === 'polygon' ? Math.max(0, (outline.coordinates[0]?.length ?? 1) - 1) : 0;

  return (
    <Flex gap={8} wrap align="center">
      <Select
        aria-label={t('filters.editor.spatial.outlineKind')}
        data-testid={`${testId}-outline-kind`}
        value={kind}
        onChange={(next: InsideOutlineKind) => {
          setKind(next);
          onChange([]);
          if (next === 'view') {
            // Choosing the view is asking for it: take it now, and again on the button.
            takeView();
          }
        }}
        options={outlineKinds.map((k) => ({
          value: k,
          label: t(`filters.editor.spatial.outlineKinds.${k}`),
        }))}
        style={{ width: 170 }}
      />
      {kind === 'area' && (
        <ObjectSelector
          scopes={anchorScopes('area')}
          presentation="dropdown"
          sorts={[]}
          value={outline?.type === 'object' ? outline.id : null}
          onChange={(id) => onChange(typeof id === 'string' ? [{ type: 'object', world: ANCHOR_WORLD, id }] : [])}
          aria-label={t('filters.editor.spatial.area')}
          data-testid={`${testId}-area`}
        />
      )}
      {(kind === 'view' || kind === 'draw') && (
        <Flex gap={8} align="center" wrap>
          <Button
            size="small"
            loading={drawing}
            onClick={kind === 'view' ? takeView : draw}
            data-testid={`${testId}-${kind === 'view' ? 'take-view' : 'draw'}`}
          >
            {t(kind === 'view' ? 'filters.editor.spatial.takeViewAgain' : outline?.type === 'polygon'
              ? 'filters.editor.spatial.drawAgain'
              : 'filters.editor.spatial.draw')}
          </Button>
          {corners > 0 && (
            <Typography.Text type="secondary" data-testid={`${testId}-outline-set`}>
              {t('filters.editor.spatial.outlineSet', { count: corners })}
            </Typography.Text>
          )}
        </Flex>
      )}
      {kind === 'polygon' && (
        <PolygonInput
          value={outline?.type === 'polygon' ? outline.coordinates : null}
          onChange={(coordinates) => onChange(coordinates === null ? [] : [{ type: 'polygon', coordinates }])}
          testId={`${testId}-polygon`}
        />
      )}
    </Flex>
  );
}

/**
 * Longitude and latitude in decimal degrees. A pair half-typed, or off the globe, is held here and
 * not written into the row, so the row stays unfinished rather than measuring from a wrong place.
 */
function PointInput({
  value,
  onChange,
  testId,
}: {
  value: { longitude: number; latitude: number } | null;
  onChange: (point: { longitude: number; latitude: number } | null) => void;
  testId: string;
}) {
  const { t } = useTranslation();
  const [lon, setLon] = useState<number | null>(value?.longitude ?? null);
  const [lat, setLat] = useState<number | null>(value?.latitude ?? null);
  const write = (nextLon: number | null, nextLat: number | null) => {
    setLon(nextLon);
    setLat(nextLat);
    onChange(isOnGlobe(nextLon, nextLat) ? { longitude: nextLon!, latitude: nextLat! } : null);
  };
  return (
    <Space.Compact>
      <InputNumber
        aria-label={t('filters.editor.spatial.longitude')}
        placeholder={t('filters.editor.spatial.longitude')}
        data-testid={`${testId}-lon`}
        min={-180}
        max={180}
        value={lon}
        onChange={(n) => write(typeof n === 'number' ? n : null, lat)}
      />
      <InputNumber
        aria-label={t('filters.editor.spatial.latitude')}
        placeholder={t('filters.editor.spatial.latitude')}
        data-testid={`${testId}-lat`}
        min={-90}
        max={90}
        value={lat}
        onChange={(n) => write(lon, typeof n === 'number' ? n : null)}
      />
    </Space.Compact>
  );
}

/**
 * A GeoJSON polygon, pasted. The text is kept as typed while it does not read as an outline, with
 * the reason shown under it; only an outline that reads is written into the row.
 */
function PolygonInput({
  value,
  onChange,
  testId,
}: {
  value: number[][][] | null;
  onChange: (coordinates: number[][][] | null) => void;
  testId: string;
}) {
  const { t } = useTranslation();
  const [text, setText] = useState(value === null ? '' : polygonText(value));
  const [problem, setProblem] = useState<PolygonProblem | null>(null);
  return (
    <Flex vertical gap={4} style={{ minWidth: 280 }}>
      <Input.TextArea
        aria-label={t('filters.editor.spatial.polygon')}
        placeholder={t('filters.editor.spatial.polygonHint')}
        data-testid={testId}
        rows={3}
        value={text}
        status={problem === null ? undefined : 'error'}
        onChange={(e) => {
          const next = e.target.value;
          setText(next);
          if (next.trim() === '') {
            setProblem(null);
            onChange(null);
            return;
          }
          const read = parsePolygon(next);
          if ('problem' in read) {
            setProblem(read.problem);
            onChange(null);
          } else {
            setProblem(null);
            onChange(read.coordinates);
          }
        }}
      />
      {problem !== null && (
        <Typography.Text type="danger" data-testid={`${testId}-problem`}>
          {t(`filters.editor.spatial.polygonProblems.${problem}`)}
        </Typography.Text>
      )}
    </Flex>
  );
}

/**
 * Another saved filter to measure from: only those this reader can run, over this world. One the
 * row already names that is not among them is shown as not available, never by its identifier.
 */
function SavedFilterPicker({
  world,
  value,
  onChange,
  testId,
}: {
  world: string;
  value: string | null;
  onChange: (id: string | null) => void;
  testId: string;
}) {
  const { t } = useTranslation();
  const { data, isLoading } = useSavedFilters();
  const usable = (data ?? []).filter((f) =>
    f.state === 'ready' && documentOf(f)?.scope.some((s) => s.world === world) === true);
  const options = usable.map((f) => ({ value: f.id, label: f.name }));
  if (value !== null && !isLoading && !usable.some((f) => f.id === value)) {
    options.push({ value, label: t('savedFilters.notAvailable') });
  }
  return (
    <Select
      aria-label={t('filters.editor.spatial.savedFilter')}
      data-testid={testId}
      value={value ?? undefined}
      loading={isLoading}
      options={options}
      onChange={(id: string | undefined) => onChange(id ?? null)}
      allowClear
      showSearch
      optionFilterProp="label"
      style={{ minWidth: 200 }}
    />
  );
}
