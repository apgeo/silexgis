// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { Checkbox, Collapse, Input, InputNumber, Segmented, Select, Slider, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useGeofiles, useMapLayers, useRasterMaps, useSavedFilters } from '../../../api/hooks.ts';
import {
  FLYBY_CHANGE_KINDS,
  FLYBY_CUT_CHANGES,
  FLYBY_LENS_RANGE,
  FLYBY_NAMED_LAYERS,
  flybyLayerKey,
  FLYBY_MAX_CHANGE_SECONDS,
  FLYBY_MAX_TEXT,
  flybyProblems,
  type FlybyChange,
  type FlybyChangeKind,
  type FlybyChangeWhat,
  type FlybyColour,
  type FlybyDocument,
  type FlybyLayerKey,
  type FlybyLook,
  type FlybySurface,
  type FlybyTexts,
} from '../../../flyby/flybyDocument.ts';
import {
  colourCaves,
  cycleBackgrounds,
  featuresInTurn,
  oldMapNewMap,
  seeThroughGround,
  type FlybyRecipeResult,
} from '../../../flyby/flybyRecipes.ts';
import { newChangeId, withLayerLook } from '../../../flyby/flybyEdit.ts';
import { FLYBY_OWN_LENS } from '../../../flyby/flybyLook.ts';
import { isDrawableIn3d } from '../../../scene3d/baseImagery3d.ts';
import { useWorkspaceStore } from '../../../stores/workspaceStore.ts';
import Button from '../../Button.tsx';
import type { FlybyController } from './useFlyby.ts';

/** The colour a control for one offers before anything was chosen. */
const DEFAULT_WALLS = '#e07020';
const DEFAULT_LINES = '#ffd23c';
const DEFAULT_SKY = '#b4c8dc';
const TITLE_SECONDS = 4;
const END_SECONDS = 4;
const FADE_IN_SECONDS = 1.5;
const FADE_OUT_SECONDS = 2;
const CAPTION_SECONDS = 6;

const percent = (share: number) => Math.round(share * 100);

/** The choice that leaves a setting as the viewer has it; a drop-down list takes no empty value. */
const OWN = 'own';

/** Names for everything a flyby can name by id, from the lists the installation keeps. */
function useLayerNames() {
  const { t } = useTranslation();
  const { data: catalog } = useMapLayers();
  const { data: rasterPage } = useRasterMaps({ pageSize: 100 });
  const { data: geofilePage } = useGeofiles({ pageSize: 100 });
  const { data: savedFilters } = useSavedFilters();
  const filterLayers = useWorkspaceStore((state) => state.filterLayers);
  return useMemo(() => {
    const layers = catalog ?? [];
    const backgrounds = layers.filter((entry) => entry.isBase && isDrawableIn3d(entry, layers));
    const rasters = rasterPage?.items ?? [];
    const filterName = (savedFilterId: string) =>
      savedFilters?.find((filter) => filter.id.toLowerCase() === savedFilterId.toLowerCase())?.name;
    const backgroundName = (id: number) =>
      layers.find((entry) => Number(entry.id) === id)?.name ?? t('flyby.look.layer.unknown');
    const layerName = (key: FlybyLayerKey): string => {
      const colon = key.indexOf(':');
      if (colon < 0) {
        return t(`flyby.look.layer.${key}`);
      }
      const id = key.slice(colon + 1);
      switch (key.slice(0, colon)) {
        case 'overlay':
          return layers.find((entry) => Number(entry.id) === Number(id))?.name ?? t('flyby.look.layer.unknown');
        case 'raster':
          return rasters.find((raster) => raster.id === id)?.name ?? t('flyby.look.layer.unknown');
        case 'file':
          return geofilePage?.items.find((file) => file.id === id)?.name ?? t('flyby.look.layer.unknown');
        default:
          return filterName(id) ?? t('flyby.look.layer.unknown');
      }
    };
    // Every layer of this installation a flyby can name: the scene's own, then the lists'.
    const layerList: { value: FlybyLayerKey; label: string }[] = [
      ...FLYBY_NAMED_LAYERS.map((key) => ({ value: key, label: t(`flyby.handLook.layerNames.${key}`) })),
      ...layers
        .filter((entry) => !entry.isBase && isDrawableIn3d(entry, layers))
        .map((entry) => ({ value: flybyLayerKey('overlay', Number(entry.id)), label: entry.name })),
      ...rasters.filter((raster) => raster.cogUrl).map((raster) => ({ value: flybyLayerKey('raster', raster.id), label: raster.name })),
      ...(geofilePage?.items ?? [])
        .filter((file) => file.importStatus === 'imported')
        .map((file) => ({ value: flybyLayerKey('file', file.id), label: file.name })),
      ...filterLayers.map((spec) => ({
        value: flybyLayerKey('filter', spec.savedFilterId),
        label: filterName(spec.savedFilterId) ?? t('flyby.look.layer.unknown'),
      })),
    ];
    return {
      layers: layerList,
      backgrounds: backgrounds.map((entry) => ({ value: Number(entry.id), label: entry.name })),
      rasters: rasters.filter((raster) => raster.cogUrl).map((raster) => ({ value: raster.id, label: raster.name })),
      filters: filterLayers.map((spec) => ({
        value: spec.savedFilterId,
        label: filterName(spec.savedFilterId) ?? t('flyby.look.layer.unknown'),
      })),
      backgroundName,
      layerName,
    };
  }, [catalog, rasterPage, geofilePage, savedFilters, filterLayers, t]);
}

/** A colour well with a name, which a keyboard reaches and a test can fill in. */
function ColourWell(props: {
  value: FlybyColour;
  onChange(value: FlybyColour): void;
  label: string;
  disabled?: boolean;
  testId: string;
}) {
  return (
    <input
      type="color"
      className="scene3d-flyby-colour"
      value={props.value}
      onChange={(event) => props.onChange(event.target.value.toLowerCase())}
      aria-label={props.label}
      disabled={props.disabled}
      data-testid={props.testId}
    />
  );
}

export interface FlybyLookPanelProps {
  flyby: FlybyController;
}

/**
 * How a flyby's scene looks, what changes during it, and what is written over it.
 *
 * Three parts: the look it opens with — a setting left unticked is whatever the viewer has —, the
 * ready-made sets of changes a press writes in, and the changes themselves as a list in words,
 * each with its moment and its length to correct. Then the texts. Everything is held while the
 * flyby plays, as the rest of the panel is.
 */
export default function FlybyLookPanel({ flyby }: FlybyLookPanelProps) {
  const { t } = useTranslation();
  const names = useLayerNames();
  const { document, look, changes, texts } = flyby;
  const busy = flyby.active;
  if (!document) {
    return null;
  }

  const sectionLabel = (id: string) => {
    const index = document.sections.findIndex((section) => section.id === id);
    return index < 0 ? id : `${index + 1}. ${t(`flyby.section.${document.sections[index].kind}`)}`;
  };
  const sectionOptions = document.sections.map((section) => ({ value: section.id, label: sectionLabel(section.id) }));

  return (
    <Collapse
      size="small"
      ghost
      items={[
        {
          key: 'look',
          label: t('flyby.look.openingTitle'),
          children: <OpeningLook look={look} setLook={flyby.setLook} backgrounds={names.backgrounds} layers={names.layers} busy={busy} />,
        },
        {
          key: 'recipes',
          label: t('flyby.look.recipesTitle'),
          children: (
            <Recipes
              document={document}
              names={names}
              sectionOptions={sectionOptions}
              busy={busy}
              apply={(result) => {
                flyby.setLook(result.look);
                flyby.setChanges(result.changes);
              }}
            />
          ),
        },
        {
          key: 'changes',
          label: t('flyby.look.changesTitle', { count: changes.length }),
          children: (
            <ChangeList
              document={document}
              changes={changes}
              setChanges={flyby.setChanges}
              sectionLabel={sectionLabel}
              names={names}
              busy={busy}
            />
          ),
        },
        {
          key: 'texts',
          label: t('flyby.look.textsTitle'),
          children: <TextsEditor document={document} texts={texts} setTexts={flyby.setTexts} sectionLabel={sectionLabel} busy={busy} />,
        },
      ]}
    />
  );
}

type Names = ReturnType<typeof useLayerNames>;

function OpeningLook({
  look,
  setLook,
  backgrounds,
  layers,
  busy,
}: {
  look: FlybyLook;
  setLook(look: FlybyLook): void;
  backgrounds: Names['backgrounds'];
  layers: Names['layers'];
  busy: boolean;
}) {
  const { t } = useTranslation();
  /** The look with one member set, or taken out when `value` is undefined. */
  const set = <K extends keyof FlybyLook>(key: K, value: FlybyLook[K] | undefined) => {
    const next = { ...look };
    if (value === undefined) {
      delete next[key];
    } else {
      next[key] = value;
    }
    setLook(next);
  };
  return (
    <div className="scene3d-flyby-group" data-testid="flyby-look">
      <label className="scene3d-flyby-field scene3d-flyby-field-wide">
        <span>{t('flyby.look.background')}</span>
        <Select
          size="small"
          value={look.background ?? OWN}
          onChange={(value: number | typeof OWN) => set('background', value === OWN ? undefined : value)}
          disabled={busy}
          aria-label={t('flyby.look.background')}
          data-testid="flyby-look-background"
          options={[{ value: OWN, label: t('flyby.look.own') }, ...backgrounds]}
        />
      </label>
      <Checkbox
        checked={look.backgroundOpacity !== undefined}
        onChange={(event) => set('backgroundOpacity', event.target.checked ? 1 : undefined)}
        disabled={busy}
        data-testid="flyby-look-opacity-on"
      >
        {t('flyby.look.backgroundOpacityOn')}
      </Checkbox>
      {look.backgroundOpacity !== undefined && (
        <div className="scene3d-flyby-field scene3d-flyby-field-wide" data-testid="flyby-look-opacity">
          <Slider
            min={0}
            max={100}
            step={5}
            value={percent(look.backgroundOpacity)}
            onChange={(value) => set('backgroundOpacity', value / 100)}
            disabled={busy}
            tooltip={{ formatter: (value) => `${value}%` }}
            ariaLabelForHandle={t('flyby.look.backgroundOpacity')}
          />
        </div>
      )}
      <div className="scene3d-flyby-row">
        <Checkbox
          checked={look.sky !== undefined}
          onChange={(event) => set('sky', event.target.checked ? DEFAULT_SKY : undefined)}
          disabled={busy}
          data-testid="flyby-look-sky-on"
        >
          {t('flyby.look.skyOn')}
        </Checkbox>
        {look.sky !== undefined && (
          <ColourWell value={look.sky} onChange={(value) => set('sky', value)} label={t('flyby.look.sky')} disabled={busy} testId="flyby-look-sky" />
        )}
      </div>
      <div className="scene3d-flyby-row">
        <Checkbox
          checked={look.walls !== undefined}
          onChange={(event) => set('walls', event.target.checked ? { colour: DEFAULT_WALLS, strength: 0.7 } : undefined)}
          disabled={busy}
          data-testid="flyby-look-walls-on"
        >
          {t('flyby.look.wallsOn')}
        </Checkbox>
        {look.walls && (
          <ColourWell
            value={look.walls.colour}
            onChange={(colour) => set('walls', { ...look.walls!, colour })}
            label={t('flyby.look.wallsColour')}
            disabled={busy}
            testId="flyby-look-walls-colour"
          />
        )}
      </div>
      {look.walls && (
        <div className="scene3d-flyby-field scene3d-flyby-field-wide" data-testid="flyby-look-walls-strength">
          <Slider
            min={0}
            max={100}
            step={5}
            value={percent(look.walls.strength)}
            onChange={(value) => set('walls', { ...look.walls!, strength: value / 100 })}
            disabled={busy}
            tooltip={{ formatter: (value) => `${value}%` }}
            ariaLabelForHandle={t('flyby.look.wallsStrength')}
          />
        </div>
      )}
      <div className="scene3d-flyby-row">
        <Checkbox
          checked={look.lines !== undefined}
          onChange={(event) => set('lines', event.target.checked ? DEFAULT_LINES : undefined)}
          disabled={busy}
          data-testid="flyby-look-lines-on"
        >
          {t('flyby.look.linesOn')}
        </Checkbox>
        {look.lines !== undefined && (
          <ColourWell
            value={look.lines}
            onChange={(value) => set('lines', value)}
            label={t('flyby.look.linesColour')}
            disabled={busy}
            testId="flyby-look-lines-colour"
          />
        )}
      </div>
      <label className="scene3d-flyby-field scene3d-flyby-field-wide">
        <span>{t('flyby.look.surface')}</span>
        <Select
          size="small"
          value={look.surface ?? OWN}
          onChange={(value: FlybySurface | typeof OWN) => set('surface', value === OWN ? undefined : value)}
          disabled={busy}
          aria-label={t('flyby.look.surface')}
          data-testid="flyby-look-surface"
          options={[
            { value: OWN, label: t('flyby.look.own') },
            { value: 'overlay', label: t('flyby.look.surfaceOverlay') },
            { value: 'cutaway', label: t('flyby.look.surfaceCutaway') },
          ]}
        />
      </label>
      <Checkbox
        checked={look.lens !== undefined}
        onChange={(event) => set('lens', event.target.checked ? FLYBY_OWN_LENS : undefined)}
        disabled={busy}
        data-testid="flyby-look-lens-on"
      >
        {t('flyby.lens.on')}
      </Checkbox>
      {look.lens !== undefined && (
        <div className="scene3d-flyby-field scene3d-flyby-field-wide" data-testid="flyby-look-lens">
          <Slider
            min={FLYBY_LENS_RANGE.min}
            max={FLYBY_LENS_RANGE.max}
            step={5}
            value={look.lens}
            onChange={(value) => set('lens', value)}
            disabled={busy}
            tooltip={{ formatter: (value) => `${value}°` }}
            ariaLabelForHandle={t('flyby.lens.degrees')}
          />
        </div>
      )}
      <OpeningLayers look={look} setLook={setLook} layers={layers} busy={busy} />
    </div>
  );
}

/**
 * Each layer of the installation as the flyby opens on it: shown, hidden, or as the viewer has it,
 * and at a strength of its own or the viewer's.
 */
function OpeningLayers({
  look,
  setLook,
  layers,
  busy,
}: {
  look: FlybyLook;
  setLook(look: FlybyLook): void;
  layers: Names['layers'];
  busy: boolean;
}) {
  const { t } = useTranslation();
  return (
    <fieldset className="scene3d-flyby-recipe" data-testid="flyby-look-layers">
      <legend>{t('flyby.handLook.layersTitle')}</legend>
      <ul className="scene3d-flyby-layers">
        {layers.map((layer) => {
          const own = look.layers?.[layer.value] ?? {};
          return (
            <li key={layer.value} data-testid={`flyby-look-layer-${layer.value}`}>
              <span className="scene3d-flyby-layer-name">{layer.label}</span>
              <Select
                size="small"
                value={own.visible === undefined ? OWN : own.visible ? 'shown' : 'hidden'}
                onChange={(value: string) =>
                  setLook(withLayerLook(look, layer.value, { visible: value === OWN ? undefined : value === 'shown' }))
                }
                disabled={busy}
                aria-label={t('flyby.handLook.layerShown', { name: layer.label })}
                data-testid={`flyby-look-layer-${layer.value}-shown`}
                options={[
                  { value: OWN, label: t('flyby.look.own') },
                  { value: 'shown', label: t('flyby.handLook.shown') },
                  { value: 'hidden', label: t('flyby.handLook.hidden') },
                ]}
              />
              <InputNumber
                size="small"
                min={0}
                max={100}
                step={10}
                value={own.opacity === undefined ? null : percent(own.opacity)}
                placeholder={t('flyby.handLook.ownStrength')}
                onChange={(value) => setLook(withLayerLook(look, layer.value, { opacity: value === null ? undefined : value / 100 }))}
                disabled={busy}
                suffix="%"
                aria-label={t('flyby.handLook.layerStrength', { name: layer.label })}
                data-testid={`flyby-look-layer-${layer.value}-strength`}
              />
            </li>
          );
        })}
      </ul>
    </fieldset>
  );
}

function Recipes({
  document,
  names,
  sectionOptions,
  busy,
  apply,
}: {
  document: FlybyDocument;
  names: Names;
  sectionOptions: { value: string; label: string }[];
  busy: boolean;
  apply(result: FlybyRecipeResult): void;
}) {
  const { t } = useTranslation();
  const orbit = document.sections.find((section) => section.kind === 'orbit')?.id ?? document.sections[0]?.id;
  const [cycle, setCycle] = useState<number[]>([]);
  const [cycleSeconds, setCycleSeconds] = useState(8);
  const [crossFade, setCrossFade] = useState(true);
  const [share, setShare] = useState(40);
  const [colour, setColour] = useState(DEFAULT_WALLS);
  const [colourFrom, setColourFrom] = useState<string>();
  const [raster, setRaster] = useState<string>();
  const [rasterSection, setRasterSection] = useState<string>();
  const [filters, setFilters] = useState<string[]>([]);
  const [filterSeconds, setFilterSeconds] = useState(6);
  const present = (id: string | undefined) => (id && document.sections.some((section) => section.id === id) ? id : orbit);

  return (
    <div className="scene3d-flyby-group" data-testid="flyby-recipes">
      <fieldset className="scene3d-flyby-recipe" data-testid="flyby-recipe-cycle">
        <legend>{t('flyby.look.recipe.cycle.name')}</legend>
        <Select
          size="small"
          mode="multiple"
          value={cycle}
          onChange={setCycle}
          disabled={busy}
          placeholder={t('flyby.look.recipe.cycle.backgrounds')}
          aria-label={t('flyby.look.recipe.cycle.backgrounds')}
          data-testid="flyby-recipe-cycle-backgrounds"
          // Searched by what a person reads, not by the ids underneath.
          optionFilterProp="label"
          options={names.backgrounds}
        />
        <div className="scene3d-flyby-row">
          <InputNumber
            size="small"
            min={2}
            max={120}
            value={cycleSeconds}
            onChange={(value) => value !== null && setCycleSeconds(value)}
            disabled={busy}
            suffix={t('flyby.secondsUnit')}
            aria-label={t('flyby.look.recipe.secondsEach')}
            data-testid="flyby-recipe-cycle-seconds"
          />
          <Checkbox checked={crossFade} onChange={(event) => setCrossFade(event.target.checked)} disabled={busy} data-testid="flyby-recipe-cycle-fade">
            {t('flyby.look.recipe.cycle.crossFade')}
          </Checkbox>
          <Button
            size="small"
            icon={<PlusOutlined />}
            disabled={busy || cycle.length < 2}
            onClick={() =>
              apply(cycleBackgrounds(document, { backgrounds: cycle, secondsEach: cycleSeconds, fadeSeconds: crossFade ? 2 : 0 }))
            }
            data-testid="flyby-recipe-cycle-add"
          >
            {t('flyby.look.recipe.add')}
          </Button>
        </div>
      </fieldset>

      <fieldset className="scene3d-flyby-recipe" data-testid="flyby-recipe-see-through">
        <legend>{t('flyby.look.recipe.seeThrough.name')}</legend>
        <div className="scene3d-flyby-row">
          <InputNumber
            size="small"
            min={0}
            max={90}
            step={10}
            value={share}
            onChange={(value) => value !== null && setShare(value)}
            disabled={busy}
            suffix="%"
            aria-label={t('flyby.look.recipe.seeThrough.share')}
            data-testid="flyby-recipe-see-through-share"
          />
          <Button
            size="small"
            icon={<PlusOutlined />}
            disabled={busy || !document.sections.some((section) => section.kind === 'orbit')}
            onClick={() => apply(seeThroughGround(document, { share: share / 100 }))}
            data-testid="flyby-recipe-see-through-add"
          >
            {t('flyby.look.recipe.add')}
          </Button>
        </div>
      </fieldset>

      <fieldset className="scene3d-flyby-recipe" data-testid="flyby-recipe-colour">
        <legend>{t('flyby.look.recipe.colour.name')}</legend>
        <div className="scene3d-flyby-row">
          <ColourWell value={colour} onChange={setColour} label={t('flyby.look.recipe.colour.colour')} disabled={busy} testId="flyby-recipe-colour-colour" />
          <Select
            size="small"
            value={present(colourFrom)}
            onChange={setColourFrom}
            disabled={busy}
            aria-label={t('flyby.look.recipe.colour.from')}
            data-testid="flyby-recipe-colour-section"
            options={sectionOptions}
          />
          <Button
            size="small"
            icon={<PlusOutlined />}
            disabled={busy || !present(colourFrom)}
            onClick={() => apply(colourCaves(document, { colour, strength: 0.7, section: present(colourFrom)! }))}
            data-testid="flyby-recipe-colour-add"
          >
            {t('flyby.look.recipe.add')}
          </Button>
        </div>
      </fieldset>

      <fieldset className="scene3d-flyby-recipe" data-testid="flyby-recipe-old-map">
        <legend>{t('flyby.look.recipe.oldMap.name')}</legend>
        {names.rasters.length === 0 ? (
          <Typography.Text type="secondary">{t('flyby.look.recipe.oldMap.none')}</Typography.Text>
        ) : (
          <div className="scene3d-flyby-row">
            <Select
              size="small"
              value={raster}
              onChange={setRaster}
              disabled={busy}
              placeholder={t('flyby.look.recipe.oldMap.raster')}
              aria-label={t('flyby.look.recipe.oldMap.raster')}
              data-testid="flyby-recipe-old-map-raster"
              options={names.rasters}
            />
            <Select
              size="small"
              value={present(rasterSection)}
              onChange={setRasterSection}
              disabled={busy}
              aria-label={t('flyby.look.recipe.oldMap.section')}
              data-testid="flyby-recipe-old-map-section"
              options={sectionOptions}
            />
            <Button
              size="small"
              icon={<PlusOutlined />}
              disabled={busy || !raster}
              onClick={() => raster && apply(oldMapNewMap(document, { raster, section: present(rasterSection)! }))}
              data-testid="flyby-recipe-old-map-add"
            >
              {t('flyby.look.recipe.add')}
            </Button>
          </div>
        )}
      </fieldset>

      <fieldset className="scene3d-flyby-recipe" data-testid="flyby-recipe-features">
        <legend>{t('flyby.look.recipe.features.name')}</legend>
        {names.filters.length === 0 ? (
          <Typography.Text type="secondary">{t('flyby.look.recipe.features.none')}</Typography.Text>
        ) : (
          <>
            <Select
              size="small"
              mode="multiple"
              value={filters}
              onChange={setFilters}
              disabled={busy}
              placeholder={t('flyby.look.recipe.features.filters')}
              aria-label={t('flyby.look.recipe.features.filters')}
              data-testid="flyby-recipe-features-filters"
              // Searched by what a person reads, not by the ids underneath.
              optionFilterProp="label"
              options={names.filters}
            />
            <div className="scene3d-flyby-row">
              <InputNumber
                size="small"
                min={2}
                max={120}
                value={filterSeconds}
                onChange={(value) => value !== null && setFilterSeconds(value)}
                disabled={busy}
                suffix={t('flyby.secondsUnit')}
                aria-label={t('flyby.look.recipe.secondsEach')}
                data-testid="flyby-recipe-features-seconds"
              />
              <Button
                size="small"
                icon={<PlusOutlined />}
                disabled={busy || filters.length === 0}
                onClick={() => apply(featuresInTurn(document, { filters, secondsEach: filterSeconds }))}
                data-testid="flyby-recipe-features-add"
              >
                {t('flyby.look.recipe.add')}
              </Button>
            </div>
          </>
        )}
      </fieldset>
    </div>
  );
}

/** A change in words: what it does, to what. */
function useChangeWords(names: Names) {
  const { t } = useTranslation();
  return (change: FlybyChange): string => {
    switch (change.kind) {
      case 'background':
        return t('flyby.look.change.background', { name: names.backgroundName(change.background) });
      case 'backgroundOpacity':
        return t('flyby.look.change.backgroundOpacity', { percent: percent(change.opacity) });
      case 'layer':
        return t(change.visible ? 'flyby.look.change.layerOn' : 'flyby.look.change.layerOff', {
          name: names.layerName(change.layer),
        });
      case 'layerOpacity':
        return t('flyby.look.change.layerOpacity', { name: names.layerName(change.layer), percent: percent(change.opacity) });
      case 'walls':
        return change.strength > 0
          ? t('flyby.look.change.walls', { colour: change.colour, percent: percent(change.strength) })
          : t('flyby.look.change.wallsOwn');
      case 'lines':
        return change.colour ? t('flyby.look.change.lines', { colour: change.colour }) : t('flyby.look.change.linesOwn');
      case 'surface':
        return t(change.surface === 'cutaway' ? 'flyby.look.change.surfaceCutaway' : 'flyby.look.change.surfaceOverlay');
      case 'sky':
        return change.colour ? t('flyby.look.change.sky', { colour: change.colour }) : t('flyby.look.change.skyNone');
      case 'lens':
        return t('flyby.lens.change', { degrees: change.degrees });
    }
  };
}

function ChangeList({
  document,
  changes,
  setChanges,
  sectionLabel,
  names,
  busy,
}: {
  document: FlybyDocument;
  changes: FlybyChange[];
  setChanges(changes: FlybyChange[]): void;
  sectionLabel(id: string): string;
  names: Names;
  busy: boolean;
}) {
  const { t } = useTranslation();
  const words = useChangeWords(names);
  // What is wrong with each change, said beside it rather than only when Play is pressed.
  const trouble = new Map<string, string>();
  for (const problem of flybyProblems(document)) {
    if (problem.code === 'overlappingChanges') {
      trouble.set(problem.change, t('flyby.look.changeClash'));
    } else if (problem.code === 'badChange' || problem.code === 'unknownChangeSection') {
      trouble.set(problem.change, t('flyby.look.changeBad'));
    }
  }
  const update = (id: string, next: Partial<Pick<FlybyChange, 'at' | 'seconds'>>) =>
    setChanges(changes.map((change) => (change.id === id ? ({ ...change, ...next } as FlybyChange) : change)));
  const add = <AddChange document={document} changes={changes} setChanges={setChanges} sectionLabel={sectionLabel} names={names} busy={busy} />;
  if (changes.length === 0) {
    return (
      <div className="scene3d-flyby-group">
        <Typography.Text type="secondary">{t('flyby.look.noChanges')}</Typography.Text>
        {add}
      </div>
    );
  }
  return (
    <div className="scene3d-flyby-group">
      <ol className="scene3d-flyby-changes" data-testid="flyby-changes">
        {changes.map((change) => {
          const section = document.sections.find((candidate) => candidate.id === change.section);
          const what = words(change);
          return (
            <li key={change.id} data-testid={`flyby-change-${change.id}`}>
              <span className="scene3d-flyby-change-what">
                {what}
                <Typography.Text type="secondary"> · {sectionLabel(change.section)}</Typography.Text>
              </span>
              <div className="scene3d-flyby-row">
                <InputNumber
                  size="small"
                  min={0}
                  max={section?.seconds ?? FLYBY_MAX_CHANGE_SECONDS}
                  step={0.5}
                  value={change.at}
                  onChange={(value) => value !== null && update(change.id, { at: value })}
                  disabled={busy}
                  prefix="+"
                  suffix={t('flyby.secondsUnit')}
                  aria-label={t('flyby.look.changeAt', { what, section: sectionLabel(change.section) })}
                  data-testid={`flyby-change-${change.id}-at`}
                />
                <InputNumber
                  size="small"
                  min={0}
                  max={FLYBY_MAX_CHANGE_SECONDS}
                  step={0.5}
                  value={FLYBY_CUT_CHANGES.includes(change.kind) ? 0 : change.seconds}
                  onChange={(value) => value !== null && update(change.id, { seconds: value })}
                  disabled={busy || FLYBY_CUT_CHANGES.includes(change.kind)}
                  suffix={t('flyby.secondsUnit')}
                  aria-label={t('flyby.look.changeSeconds', { what })}
                  data-testid={`flyby-change-${change.id}-seconds`}
                />
                <Button
                  size="small"
                  type="text"
                  icon={<DeleteOutlined />}
                  disabled={busy}
                  onClick={() => setChanges(changes.filter((other) => other.id !== change.id))}
                  aria-label={t('flyby.look.removeChange', { what })}
                  data-testid={`flyby-change-${change.id}-remove`}
                />
              </div>
              {trouble.has(change.id) && (
                <Typography.Text type="danger" data-testid={`flyby-change-${change.id}-trouble`}>
                  {trouble.get(change.id)}
                </Typography.Text>
              )}
            </li>
          );
        })}
      </ol>
      {add}
    </div>
  );
}

/** What a new change of each kind asks for before anybody sets it. */
function defaultChange(kind: FlybyChangeKind, names: Names): FlybyChangeWhat | undefined {
  const layer = names.layers[0]?.value;
  switch (kind) {
    case 'background':
      return names.backgrounds[0] ? { kind, background: names.backgrounds[0].value } : undefined;
    case 'backgroundOpacity':
      return { kind, opacity: 0.5 };
    case 'layer':
      return layer ? { kind, layer, visible: false } : undefined;
    case 'layerOpacity':
      return layer ? { kind, layer, opacity: 0.5 } : undefined;
    case 'walls':
      return { kind, colour: DEFAULT_WALLS, strength: 0.7 };
    case 'lines':
      return { kind, colour: DEFAULT_LINES };
    case 'surface':
      return { kind, surface: 'cutaway' };
    case 'sky':
      return { kind, colour: DEFAULT_SKY };
    case 'lens':
      return { kind, degrees: 30 };
  }
}

/**
 * A change written by hand: what it changes and to what, the section it belongs to, the seconds
 * into it that it starts and how long it takes. Added to the list, where its numbers can be
 * corrected like those of a ready-made one.
 */
function AddChange({
  document,
  changes,
  setChanges,
  sectionLabel,
  names,
  busy,
}: {
  document: FlybyDocument;
  changes: FlybyChange[];
  setChanges(changes: FlybyChange[]): void;
  sectionLabel(id: string): string;
  names: Names;
  busy: boolean;
}) {
  const { t } = useTranslation();
  const [what, setWhat] = useState<FlybyChangeWhat>({ kind: 'backgroundOpacity', opacity: 0.5 });
  const [sectionChoice, setSection] = useState<string>();
  const [at, setAt] = useState(0);
  const [seconds, setSeconds] = useState(2);
  const section = document.sections.find((candidate) => candidate.id === sectionChoice) ?? document.sections[0];
  const cut = FLYBY_CUT_CHANGES.includes(what.kind);
  const kinds = FLYBY_CHANGE_KINDS.filter((kind) => defaultChange(kind, names) !== undefined);
  const layerSelect = (layer: FlybyLayerKey, onChange: (layer: FlybyLayerKey) => void) => (
    <Select
      size="small"
      className="scene3d-flyby-grow"
      value={layer}
      onChange={onChange}
      disabled={busy}
      aria-label={t('flyby.handLook.layer')}
      data-testid="flyby-add-change-layer"
      optionFilterProp="label"
      showSearch
      options={names.layers}
    />
  );
  const share = (value: number, onChange: (value: number) => void, label: string) => (
    <InputNumber
      size="small"
      min={0}
      max={100}
      step={10}
      value={percent(value)}
      onChange={(next) => next !== null && onChange(next / 100)}
      disabled={busy}
      suffix="%"
      aria-label={label}
      data-testid="flyby-add-change-share"
    />
  );

  const target = (() => {
    switch (what.kind) {
      case 'background':
        return (
          <Select
            size="small"
            className="scene3d-flyby-grow"
            value={what.background}
            onChange={(background: number) => setWhat({ ...what, background })}
            disabled={busy}
            aria-label={t('flyby.look.background')}
            data-testid="flyby-add-change-background"
            optionFilterProp="label"
            options={names.backgrounds}
          />
        );
      case 'backgroundOpacity':
        return share(what.opacity, (opacity) => setWhat({ ...what, opacity }), t('flyby.look.backgroundOpacity'));
      case 'layer':
        return (
          <>
            {layerSelect(what.layer, (layer) => setWhat({ ...what, layer }))}
            <Segmented
              size="small"
              value={what.visible ? 'shown' : 'hidden'}
              onChange={(value) => setWhat({ ...what, visible: value === 'shown' })}
              disabled={busy}
              aria-label={t('flyby.handLook.layerTo')}
              data-testid="flyby-add-change-visible"
              options={[
                { value: 'shown', label: t('flyby.handLook.shown') },
                { value: 'hidden', label: t('flyby.handLook.hidden') },
              ]}
            />
          </>
        );
      case 'layerOpacity':
        return (
          <>
            {layerSelect(what.layer, (layer) => setWhat({ ...what, layer }))}
            {share(what.opacity, (opacity) => setWhat({ ...what, opacity }), t('flyby.handLook.strength'))}
          </>
        );
      case 'walls':
        return (
          <>
            <ColourWell value={what.colour} onChange={(colour) => setWhat({ ...what, colour })} label={t('flyby.look.wallsColour')} disabled={busy} testId="flyby-add-change-colour" />
            {share(what.strength, (strength) => setWhat({ ...what, strength }), t('flyby.look.wallsStrength'))}
          </>
        );
      case 'lines':
      case 'sky':
        return (
          <>
            <Checkbox
              checked={what.colour === null}
              onChange={(event) => setWhat({ ...what, colour: event.target.checked ? null : what.kind === 'lines' ? DEFAULT_LINES : DEFAULT_SKY })}
              disabled={busy}
              data-testid="flyby-add-change-own"
            >
              {t(what.kind === 'lines' ? 'flyby.handLook.linesByDepth' : 'flyby.handLook.noSky')}
            </Checkbox>
            {what.colour !== null && (
              <ColourWell
                value={what.colour}
                onChange={(colour) => setWhat({ ...what, colour })}
                label={t(what.kind === 'lines' ? 'flyby.look.linesColour' : 'flyby.look.sky')}
                disabled={busy}
                testId="flyby-add-change-colour"
              />
            )}
          </>
        );
      case 'lens':
        return (
          <InputNumber
            size="small"
            min={FLYBY_LENS_RANGE.min}
            max={FLYBY_LENS_RANGE.max}
            step={5}
            value={what.degrees}
            onChange={(degrees) => degrees !== null && setWhat({ ...what, degrees })}
            disabled={busy}
            suffix="°"
            aria-label={t('flyby.lens.degrees')}
            data-testid="flyby-add-change-lens"
          />
        );
      case 'surface':
        return (
          <Select
            size="small"
            className="scene3d-flyby-grow"
            value={what.surface}
            onChange={(surface: FlybySurface) => setWhat({ ...what, surface })}
            disabled={busy}
            aria-label={t('flyby.look.surface')}
            data-testid="flyby-add-change-surface"
            options={[
              { value: 'overlay', label: t('flyby.look.surfaceOverlay') },
              { value: 'cutaway', label: t('flyby.look.surfaceCutaway') },
            ]}
          />
        );
    }
  })();

  return (
    <fieldset className="scene3d-flyby-recipe" data-testid="flyby-add-change">
      <legend>{t('flyby.handLook.addTitle')}</legend>
      <Select
        size="small"
        value={what.kind}
        onChange={(kind: FlybyChangeKind) => setWhat(defaultChange(kind, names) ?? what)}
        disabled={busy}
        aria-label={t('flyby.handLook.kind')}
        data-testid="flyby-add-change-kind"
        options={kinds.map((kind) => ({ value: kind, label: t(`flyby.handLook.kinds.${kind}`) }))}
      />
      <div className="scene3d-flyby-row">{target}</div>
      <div className="scene3d-flyby-row">
        <Select
          size="small"
          className="scene3d-flyby-grow"
          value={section?.id}
          onChange={setSection}
          disabled={busy}
          aria-label={t('flyby.handLook.section')}
          data-testid="flyby-add-change-section"
          options={document.sections.map((candidate) => ({ value: candidate.id, label: sectionLabel(candidate.id) }))}
        />
        <InputNumber
          size="small"
          min={0}
          max={section?.seconds ?? FLYBY_MAX_CHANGE_SECONDS}
          step={0.5}
          value={at}
          onChange={(value) => value !== null && setAt(value)}
          disabled={busy}
          prefix="+"
          suffix={t('flyby.secondsUnit')}
          aria-label={t('flyby.handLook.at')}
          data-testid="flyby-add-change-at"
        />
        <InputNumber
          size="small"
          min={0}
          max={FLYBY_MAX_CHANGE_SECONDS}
          step={0.5}
          value={cut ? 0 : seconds}
          onChange={(value) => value !== null && setSeconds(value)}
          disabled={busy || cut}
          suffix={t('flyby.secondsUnit')}
          aria-label={t('flyby.handLook.seconds')}
          data-testid="flyby-add-change-seconds"
        />
      </div>
      <Button
        size="small"
        icon={<PlusOutlined />}
        disabled={busy || !section}
        onClick={() =>
          section &&
          setChanges([
            ...changes,
            {
              ...what,
              id: newChangeId(changes, what.kind),
              section: section.id,
              at: Math.min(at, section.seconds),
              seconds: cut ? 0 : seconds,
            } as FlybyChange,
          ])
        }
        data-testid="flyby-add-change-add"
      >
        {t('flyby.handLook.add')}
      </Button>
    </fieldset>
  );
}

function TextsEditor({
  document,
  texts,
  setTexts,
  sectionLabel,
  busy,
}: {
  document: FlybyDocument;
  texts: FlybyTexts;
  setTexts(texts: FlybyTexts): void;
  sectionLabel(id: string): string;
  busy: boolean;
}) {
  const { t } = useTranslation();
  const set = <K extends keyof FlybyTexts>(key: K, value: FlybyTexts[K] | undefined) => {
    const next = { ...texts };
    if (value === undefined) {
      delete next[key];
    } else {
      next[key] = value;
    }
    setTexts(next);
  };
  /** A card from what was typed: none when its text is empty, the subtitle left out when empty. */
  const card = (text: string, subtitle: string | undefined, seconds: number) =>
    text.trim().length === 0 ? undefined : { text, ...(subtitle?.trim() ? { subtitle } : {}), seconds };
  const caption = (sectionId: string) => texts.captions?.find((entry) => entry.section === sectionId)?.text ?? '';
  const setCaption = (sectionId: string, text: string) => {
    const section = document.sections.find((candidate) => candidate.id === sectionId);
    const others = (texts.captions ?? []).filter((entry) => entry.section !== sectionId);
    const captions =
      text.trim().length > 0 && section
        ? [...others, { section: sectionId, text, at: 0, seconds: Math.min(CAPTION_SECONDS, section.seconds) }]
        : others;
    set('captions', captions.length > 0 ? captions : undefined);
  };
  return (
    <div className="scene3d-flyby-group" data-testid="flyby-texts-editor">
      <Input
        size="small"
        value={texts.title?.text ?? ''}
        onChange={(event) => set('title', card(event.target.value, texts.title?.subtitle, texts.title?.seconds ?? TITLE_SECONDS))}
        maxLength={FLYBY_MAX_TEXT}
        disabled={busy}
        placeholder={t('flyby.look.title')}
        aria-label={t('flyby.look.title')}
        data-testid="flyby-text-title"
      />
      <Input
        size="small"
        value={texts.title?.subtitle ?? ''}
        onChange={(event) => texts.title && set('title', card(texts.title.text, event.target.value, texts.title.seconds))}
        maxLength={FLYBY_MAX_TEXT}
        disabled={busy || !texts.title}
        placeholder={t('flyby.look.subtitle')}
        aria-label={t('flyby.look.subtitle')}
        data-testid="flyby-text-subtitle"
      />
      <Typography.Text type="secondary">{t('flyby.look.captions')}</Typography.Text>
      {document.sections.map((section) => (
        <Input
          key={section.id}
          size="small"
          value={caption(section.id)}
          onChange={(event) => setCaption(section.id, event.target.value)}
          maxLength={FLYBY_MAX_TEXT}
          disabled={busy}
          placeholder={sectionLabel(section.id)}
          aria-label={t('flyby.look.captionFor', { section: sectionLabel(section.id) })}
          data-testid={`flyby-text-caption-${section.id}`}
        />
      ))}
      <Input
        size="small"
        value={texts.end?.text ?? ''}
        onChange={(event) => set('end', card(event.target.value, texts.end?.subtitle, texts.end?.seconds ?? END_SECONDS))}
        maxLength={FLYBY_MAX_TEXT}
        disabled={busy}
        placeholder={t('flyby.look.end')}
        aria-label={t('flyby.look.end')}
        data-testid="flyby-text-end"
      />
      <Input
        size="small"
        value={texts.end?.subtitle ?? ''}
        onChange={(event) => texts.end && set('end', card(texts.end.text, event.target.value, texts.end.seconds))}
        maxLength={FLYBY_MAX_TEXT}
        disabled={busy || !texts.end}
        placeholder={t('flyby.look.endSubtitle')}
        aria-label={t('flyby.look.endSubtitle')}
        data-testid="flyby-text-end-subtitle"
      />
      <Checkbox
        checked={(texts.fadeIn ?? 0) > 0}
        onChange={(event) => set('fadeIn', event.target.checked ? FADE_IN_SECONDS : undefined)}
        disabled={busy}
        data-testid="flyby-text-fade-in"
      >
        {t('flyby.look.fadeIn')}
      </Checkbox>
      <Checkbox
        checked={(texts.fadeOut ?? 0) > 0}
        onChange={(event) => set('fadeOut', event.target.checked ? FADE_OUT_SECONDS : undefined)}
        disabled={busy}
        data-testid="flyby-text-fade-out"
      >
        {t('flyby.look.fadeOut')}
      </Checkbox>
      <Checkbox
        checked={texts.northArrow === true}
        onChange={(event) => set('northArrow', event.target.checked ? true : undefined)}
        disabled={busy}
        data-testid="flyby-text-north"
      >
        {t('flyby.frame.north')}
      </Checkbox>
      <Input
        size="small"
        value={texts.corner ?? ''}
        onChange={(event) => set('corner', event.target.value.trim().length > 0 ? event.target.value : undefined)}
        maxLength={FLYBY_MAX_TEXT}
        disabled={busy}
        placeholder={t('flyby.frame.corner')}
        aria-label={t('flyby.frame.corner')}
        data-testid="flyby-text-corner"
      />
    </div>
  );
}
