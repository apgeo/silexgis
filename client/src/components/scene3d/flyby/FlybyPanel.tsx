// SPDX-License-Identifier: AGPL-3.0-or-later
import { useRef } from 'react';
import {
  CloseOutlined,
  DeleteOutlined,
  DownloadOutlined,
  FolderOpenOutlined,
  NodeIndexOutlined,
  ReloadOutlined,
  SaveOutlined,
} from '@ant-design/icons';
import { Alert, Checkbox, Collapse, Input, InputNumber, Radio, Segmented, Select, Slider, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useCaveNames, useGeofiles } from '../../../api/hooks.ts';
import { saveBlob } from '../../../api/download.ts';
import {
  FLYBY_PRESETS,
  FLYBY_SPEED_RANGE,
  flybyHoldsPlace,
  type FlybyDocument,
  type FlybySubjectRef,
} from '../../../flyby/flybyDocument.ts';
import { FLYBY_FILE_MAX_BYTES, flybyFileName, flybyFileText, parseFlybyFile } from '../../../flyby/flybyLibrary.ts';
import {
  FLYBY_PAUSE_SECONDS_RANGE,
  FLYBY_SECONDS_PER_TURN_RANGE,
  FLYBY_TURNS_RANGE,
  presetFits,
  type FlybyPresetOptions,
} from '../../../flyby/flybyPresets.ts';
import { useFlybyStore } from '../../../stores/flybyStore.ts';
import Button from '../../Button.tsx';
import FlybyLookPanel from './FlybyLookPanel.tsx';
import FlybySections from './FlybySections.tsx';
import FlybyViews from './FlybyViews.tsx';
import type { FlybyController, FlybySource, FlybyTrouble } from './useFlyby.ts';

/** The problems that are about the look, the changes and the texts rather than the camera. */
const LOOK_PROBLEMS = new Set(['badLook', 'unknownChangeSection', 'badChange', 'duplicateChange', 'overlappingChanges', 'badText']);

export interface FlybyPanelProps {
  flyby: FlybyController;
  onClose(): void;
}

function troubleText(trouble: FlybyTrouble, t: (key: string, values?: Record<string, unknown>) => string): string {
  switch (trouble.kind) {
    case 'problems':
      // A change or a text that cannot be placed is said as such: the sections may all be fine.
      return trouble.problems.every((problem) => LOOK_PROBLEMS.has(problem.code))
        ? t('flyby.look.trouble')
        : t('flyby.trouble.problems');
    case 'missing':
      return t('flyby.trouble.missing');
    case 'noScene':
      return t('flyby.trouble.noScene');
    case 'badFile':
      return t('flyby.trouble.badFile');
    case 'failed':
      return t('flyby.trouble.failed', { detail: trouble.detail });
  }
}

/**
 * The flyby tool's panel: what the flyby is about, how it is built, its sections, and keeping it.
 *
 * Everything here is controlled by the hook that owns the flyby; the panel holds nothing of its
 * own but the file input. While a flyby is being prepared or played, what it is built from cannot
 * be changed — a change would end the flight in progress — so those controls are held and the
 * transport bar under the scene is what the viewer uses.
 */
export default function FlybyPanel({ flyby, onClose }: FlybyPanelProps) {
  const { t } = useTranslation();
  const fileInput = useRef<HTMLInputElement>(null);
  const library = useFlybyStore((state) => state.library);
  const saveFlyby = useFlybyStore((state) => state.save);
  const removeFlyby = useFlybyStore((state) => state.remove);
  const loadRoute = useFlybyStore((state) => state.loadRoute);
  const setLoadRoute = useFlybyStore((state) => state.setLoadRoute);

  const { document } = flyby;
  const busy = flyby.active;
  // Names for every cave the panel may have to call something: those in view, and those a flyby
  // that was opened names by id alone.
  const namedIds = [
    ...new Set([
      ...flyby.caves.map((cave) => cave.caveId),
      ...(document?.subjects.flatMap((subject) => (subject.kind === 'cave' ? [subject.caveId] : [])) ?? []),
    ]),
  ];
  const caveNames = useCaveNames(namedIds);
  // The imported files that hold something to follow: what a flyby along a line can be about.
  const { data: geofilePage } = useGeofiles({ pageSize: 100 });
  const files = (geofilePage?.items ?? []).filter((file) => file.importStatus === 'imported');
  const caveLabel = (caveId: string) =>
    caveNames.get(caveId) ?? flyby.caves.find((cave) => cave.caveId === caveId)?.label ?? t('flyby.unnamedCave');

  const subjectLabel = (ref: FlybySubjectRef): string => {
    if (ref === 'all') {
      return t('flyby.subject.all');
    }
    const subject = document?.subjects[ref];
    switch (subject?.kind) {
      case 'cave':
        return caveLabel(subject.caveId);
      case 'feature':
        return t('flyby.subject.feature');
      case 'view':
        return t('flyby.subject.view');
      case 'file':
        return files.find((file) => file.id === subject.geofileId)?.name ?? t('flyby.subject.file');
      case 'place':
        // A view captured from the camera is called by its place in the list of views.
        return subject.height !== undefined ? t('flyby.views.name', { number: ref + 1 }) : t('flyby.subject.place');
      default:
        return '';
    }
  };

  const openFile = async (file: File | undefined) => {
    if (!file) {
      return;
    }
    const opened = file.size <= FLYBY_FILE_MAX_BYTES ? parseFlybyFile(await file.text()) : null;
    if (opened) {
      flyby.setTrouble(undefined);
      flyby.open(opened);
    } else {
      flyby.setTrouble({ kind: 'badFile' });
    }
  };

  const saveFile = (saved: FlybyDocument) =>
    saveBlob(new Blob([flybyFileText(saved)], { type: 'application/json' }), flybyFileName(saved));

  const sourceOptions: { value: FlybySource; label: string; disabled?: boolean }[] = [
    { value: 'selected', label: t('flyby.source.selected'), disabled: !flyby.selectedSubject },
    { value: 'inView', label: t('flyby.source.inView') },
    { value: 'view', label: t('flyby.source.view') },
    { value: 'file', label: t('flyby.source.file') },
  ];

  return (
    <section className="scene3d-flyby-panel" data-testid="flyby-panel" aria-label={t('flyby.title')}>
      <header className="scene3d-flyby-panel-header">
        <Typography.Title level={5}>{t('flyby.title')}</Typography.Title>
        <Button
          size="small"
          type="text"
          icon={<CloseOutlined />}
          aria-label={t('common.close')}
          onClick={onClose}
          data-testid="flyby-close"
        />
      </header>

      {flyby.trouble && (
        <Alert
          type="error"
          showIcon
          closable
          onClose={() => flyby.setTrouble(undefined)}
          title={troubleText(flyby.trouble, t)}
          data-testid="flyby-problem"
        />
      )}

      {(['descent', 'follow'] as const).map((kind) =>
        // Said once the flyby has been prepared, which is when the line to follow was looked for.
        document?.sections.some((section) => section.kind === kind && flyby.lineFallbacks.includes(section.id)) ? (
          <Alert
            key={kind}
            type="info"
            showIcon
            title={t(kind === 'descent' ? 'flyby.routes.fallback' : 'flyby.routes.noLine')}
            data-testid={`flyby-line-fallback-${kind}`}
          />
        ) : null,
      )}

      <div className="scene3d-flyby-group">
        <Typography.Text strong>{t('flyby.aboutTitle')}</Typography.Text>
        <Radio.Group
          value={flyby.source}
          onChange={(event) => flyby.setSource(event.target.value as FlybySource)}
          disabled={busy}
          aria-label={t('flyby.aboutTitle')}
          data-testid="flyby-source"
        >
          {sourceOptions.map((option) => (
            <Radio key={option.value} value={option.value} disabled={option.disabled} data-testid={`flyby-source-${option.value}`}>
              {option.label}
            </Radio>
          ))}
        </Radio.Group>
        {flyby.source === 'selected' && !flyby.selectedSubject && (
          <Typography.Text type="secondary">{t('flyby.source.nothingSelected')}</Typography.Text>
        )}
        {flyby.source === 'file' &&
          (files.length === 0 ? (
            <Typography.Text type="secondary">{t('flyby.source.noFiles')}</Typography.Text>
          ) : (
            <Select
              size="small"
              value={flyby.fileId}
              onChange={flyby.setFileId}
              disabled={busy}
              placeholder={t('flyby.source.pickFile')}
              aria-label={t('flyby.source.pickFile')}
              data-testid="flyby-file"
              showSearch
              optionFilterProp="label"
              options={files.map((file) => ({ value: file.id, label: file.name }))}
            />
          ))}
        {flyby.source === 'inView' && (
          <div className="scene3d-flyby-caves">
            {flyby.caves.length === 0 ? (
              <Typography.Text type="secondary">{t('flyby.source.noCaves')}</Typography.Text>
            ) : (
              <Checkbox.Group
                value={flyby.checked}
                disabled={busy}
                // Kept in the order they were ticked, which is the order a tour visits them in.
                onChange={(values) => {
                  const next = values.map(String);
                  flyby.setCheckedCaves([
                    ...flyby.checked.filter((id) => next.includes(id)),
                    ...next.filter((id) => !flyby.checked.includes(id)),
                  ]);
                }}
                aria-label={t('flyby.source.inView')}
              >
                {flyby.caves.map((cave) => (
                  <Checkbox key={cave.caveId} value={cave.caveId} data-testid={`flyby-cave-${cave.caveId}`}>
                    {caveLabel(cave.caveId)}
                  </Checkbox>
                ))}
              </Checkbox.Group>
            )}
            <div className="scene3d-flyby-row">
              <Button size="small" icon={<ReloadOutlined />} onClick={flyby.refreshCaves} disabled={busy} data-testid="flyby-caves-refresh">
                {t('flyby.source.refresh')}
              </Button>
              <Button
                size="small"
                icon={<NodeIndexOutlined />}
                onClick={() => void flyby.orderByRoute()}
                disabled={busy || flyby.checked.length < 3}
                data-testid="flyby-caves-route"
              >
                {t('flyby.source.shortestRoute')}
              </Button>
            </div>
          </div>
        )}
      </div>

      <div className="scene3d-flyby-group">
        <Typography.Text strong>{t('flyby.presetTitle')}</Typography.Text>
        <Select
          value={flyby.preset}
          onChange={flyby.setPreset}
          // Not held for want of a document: a flyby through captured views has none until a view
          // is added, and the list is how one gets there and back.
          disabled={busy}
          aria-label={t('flyby.presetTitle')}
          data-testid="flyby-preset"
          options={FLYBY_PRESETS.map((preset) => ({
            value: preset,
            label: t(`flyby.preset.${preset}.name`),
            disabled: !presetFits(preset, document?.subjects ?? []),
          }))}
        />
        <Typography.Text type="secondary">{t(`flyby.preset.${flyby.preset}.hint`)}</Typography.Text>
        {flyby.preset === 'myViews' && (
          <FlybyViews views={flyby.views} setViews={flyby.setViews} capture={flyby.captureView} busy={busy} />
        )}
        {flyby.edited && (
          <Typography.Text type="secondary" data-testid="flyby-edited">
            {t('flyby.editedNote')}
          </Typography.Text>
        )}
      </div>

      <div className="scene3d-flyby-group">
        <Typography.Text strong>{t('flyby.optionsTitle')}</Typography.Text>
        <label className="scene3d-flyby-field">
          <span>{t('flyby.turns')}</span>
          <InputNumber
            size="small"
            min={FLYBY_TURNS_RANGE.min}
            max={FLYBY_TURNS_RANGE.max}
            step={0.25}
            value={flyby.options.turns}
            onChange={(value) => value !== null && flyby.setOptions({ turns: value })}
            disabled={busy}
            aria-label={t('flyby.turns')}
            data-testid="flyby-turns"
          />
        </label>
        <label className="scene3d-flyby-field">
          <span>{t('flyby.secondsPerTurn')}</span>
          <InputNumber
            size="small"
            min={FLYBY_SECONDS_PER_TURN_RANGE.min}
            max={FLYBY_SECONDS_PER_TURN_RANGE.max}
            step={2}
            value={flyby.options.secondsPerTurn}
            onChange={(value) => value !== null && flyby.setOptions({ secondsPerTurn: value })}
            disabled={busy}
            aria-label={t('flyby.secondsPerTurn')}
            data-testid="flyby-seconds-per-turn"
          />
        </label>
        <div className="scene3d-flyby-field scene3d-flyby-field-wide">
          <span>{t('flyby.direction')}</span>
          <Segmented
            block
            size="small"
            value={flyby.options.clockwise ? 'clockwise' : 'counterclockwise'}
            onChange={(value) => flyby.setOptions({ clockwise: value === 'clockwise' })}
            disabled={busy}
            aria-label={t('flyby.direction')}
            data-testid="flyby-direction"
            options={[
              { value: 'clockwise', label: t('flyby.clockwise') },
              { value: 'counterclockwise', label: t('flyby.counterclockwise') },
            ]}
          />
        </div>
        <Checkbox
          checked={flyby.options.underground}
          onChange={(event) => flyby.setOptions({ underground: event.target.checked })}
          disabled={busy || (flyby.preset !== 'aroundCave' && flyby.preset !== 'tour')}
          data-testid="flyby-underground"
        >
          {t('flyby.underground')}
        </Checkbox>
        <label className="scene3d-flyby-field">
          <span>{t('flyby.atCave.seconds')}</span>
          <InputNumber
            size="small"
            min={FLYBY_PAUSE_SECONDS_RANGE.min}
            max={FLYBY_PAUSE_SECONDS_RANGE.max}
            step={1}
            value={flyby.options.pauseSeconds}
            onChange={(value) => value !== null && flyby.setOptions({ pauseSeconds: value })}
            disabled={busy || flyby.preset !== 'tour'}
            suffix={t('flyby.secondsUnit')}
            aria-label={t('flyby.atCave.seconds')}
            data-testid="flyby-pause-seconds"
          />
        </label>
        {flyby.preset === 'tour' && flyby.options.pauseSeconds > 0 && (
          <label className="scene3d-flyby-field scene3d-flyby-field-wide">
            <span>{t('flyby.atCave.say')}</span>
            <Select
              size="small"
              value={flyby.options.pauseSay}
              onChange={(value: FlybyPresetOptions['pauseSay']) => flyby.setOptions({ pauseSay: value })}
              disabled={busy}
              aria-label={t('flyby.atCave.say')}
              data-testid="flyby-pause-say"
              options={[
                { value: 'none', label: t('flyby.atCave.sayNone') },
                { value: 'name', label: t('flyby.atCave.sayName') },
                { value: 'nameAndSize', label: t('flyby.atCave.sayNameAndSize') },
              ]}
            />
          </label>
        )}
        <Checkbox
          checked={flyby.options.fromFar}
          onChange={(event) => flyby.setOptions({ fromFar: event.target.checked })}
          disabled={busy}
          data-testid="flyby-from-far"
        >
          {t('flyby.fromFar')}
        </Checkbox>
        {/* The test id is on the wrapper and the name on the handle: the slider hands neither of
            its own attributes to the element a reader or a test would look for them on. */}
        <div className="scene3d-flyby-field scene3d-flyby-field-wide" data-testid="flyby-speed">
          <span>{t('flyby.speed', { speed: flyby.speed })}</span>
          <Slider
            min={FLYBY_SPEED_RANGE.min}
            max={FLYBY_SPEED_RANGE.max}
            step={0.25}
            value={flyby.speed}
            onChange={flyby.setSpeed}
            disabled={busy}
            tooltip={{ formatter: (value) => `×${value}` }}
            ariaLabelForHandle={t('flyby.speedLabel')}
          />
        </div>
        <Checkbox checked={loadRoute} onChange={(event) => setLoadRoute(event.target.checked)} disabled={busy} data-testid="flyby-load-route">
          {t('flyby.loadRoute')}
        </Checkbox>
      </div>

      {document && (
        <Collapse
          size="small"
          ghost
          items={[
            {
              key: 'sections',
              label: t('flyby.sectionsTitle', { count: document.sections.length }),
              children: (
                <FlybySections
                  document={document}
                  current={flyby.playerState.status !== 'idle' ? flyby.playerState.section : undefined}
                  busy={busy}
                  subjectLabel={subjectLabel}
                  onEdit={flyby.edit}
                />
              ),
            },
          ]}
        />
      )}

      <FlybyLookPanel flyby={flyby} />

      <div className="scene3d-flyby-group">
        <Typography.Text strong>{t('flyby.keepTitle')}</Typography.Text>
        <div className="scene3d-flyby-row">
          <Input
            size="small"
            value={flyby.name}
            onChange={(event) => flyby.setName(event.target.value)}
            maxLength={120}
            placeholder={t('flyby.namePlaceholder')}
            aria-label={t('flyby.name')}
            data-testid="flyby-name"
          />
          <Button
            size="small"
            icon={<SaveOutlined />}
            disabled={!document || flyby.name.trim().length === 0}
            onClick={() => document && saveFlyby({ ...document, name: flyby.name.trim() })}
            data-testid="flyby-save"
          >
            {t('common.save')}
          </Button>
        </div>
        {library.length > 0 && (
          <div className="scene3d-flyby-row">
            <Select
              size="small"
              className="scene3d-flyby-saved"
              placeholder={t('flyby.openSaved')}
              value={null}
              disabled={busy}
              onChange={(value: string | null) => {
                const kept = library.find((entry) => entry.name === value);
                if (kept) {
                  flyby.setTrouble(undefined);
                  flyby.open(kept);
                }
              }}
              aria-label={t('flyby.openSaved')}
              data-testid="flyby-saved"
              options={library.map((entry) => ({ value: entry.name, label: entry.name }))}
            />
            <Button
              size="small"
              danger
              icon={<DeleteOutlined />}
              disabled={!library.some((entry) => entry.name === flyby.name.trim())}
              onClick={() => removeFlyby(flyby.name.trim())}
              aria-label={t('flyby.deleteSaved')}
              data-testid="flyby-delete"
            />
          </div>
        )}
        <div className="scene3d-flyby-row">
          <Button
            size="small"
            icon={<DownloadOutlined />}
            disabled={!document}
            onClick={() => document && saveFile(document)}
            data-testid="flyby-file-save"
          >
            {t('flyby.saveFile')}
          </Button>
          <Button size="small" icon={<FolderOpenOutlined />} disabled={busy} onClick={() => fileInput.current?.click()} data-testid="flyby-file-open">
            {t('flyby.openFile')}
          </Button>
          <input
            ref={fileInput}
            type="file"
            accept=".json,application/json"
            hidden
            data-testid="flyby-file-input"
            onChange={(event) => {
              void openFile(event.target.files?.[0]);
              // Emptied so that choosing the same file again is still a change.
              event.target.value = '';
            }}
          />
        </div>
        {document && flybyHoldsPlace(document) && (
          <Typography.Text type="secondary" data-testid="flyby-holds-place">
            {t('flyby.holdsPlace')}
          </Typography.Text>
        )}
      </div>
    </section>
  );
}
