// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  ArrowDownOutlined,
  ArrowUpOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
} from '@ant-design/icons';
import { Checkbox, InputNumber, Segmented, Select, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  FLYBY_LIFT_LIMIT,
  FLYBY_MAX_CLEARANCE,
  FLYBY_MAX_RANGE,
  FLYBY_MAX_TURN_DEGREES,
  FLYBY_MIN_RANGE,
  FLYBY_RANGE_M,
  FLYBY_SECONDS_RANGE,
  FLYBY_SECTION_KINDS,
  flybyProblems,
  sectionLength,
  type FlybyAim,
  type FlybyDocument,
  type FlybySay,
  type FlybySection,
  type FlybySectionKind,
  type FlybySubjectRef,
} from '../../../flyby/flybyDocument.ts';
import {
  addSection,
  duplicateSection,
  FLYBY_ADDABLE_KINDS,
  moveSection,
  removeSection,
  updateSection,
  type FlybySectionPatch,
} from '../../../flyby/flybyEdit.ts';
import Button from '../../Button.tsx';
import { flybyClock } from './flybyClock.ts';

/** The speeds offered for one section; the whole flyby's speed is a slider. */
const SECTION_SPEEDS = [0.25, 0.5, 1, 2, 4];

/** The choice that leaves a member of the pose as the section before left it; a list takes no empty value. */
const BEFORE = 'before';

/** The section problems the list says beside the section they are about. */
const SECTION_PROBLEMS = new Set([
  'badSeconds',
  'badSpeed',
  'badRange',
  'badPitch',
  'badPose',
  'unknownSubject',
  'duplicateSection',
  'badEach',
]);

export interface FlybySectionsProps {
  document: FlybyDocument;
  /** The section being flown, by its place in the list; undefined when nothing plays. */
  current?: number;
  busy: boolean;
  subjectLabel(ref: FlybySubjectRef): string;
  /** Hands the edited document back; the hook makes it the flyby being shown and played. */
  onEdit(next: FlybyDocument): void;
}

/**
 * A flyby's sections as a list that can be edited: each section's length and speed on its line,
 * buttons to move it, copy it or take it out, and — opened under it — everything it does. A new
 * section of any kind is added at the foot of the list.
 */
export default function FlybySections({ document, current, busy, subjectLabel, onEdit }: FlybySectionsProps) {
  const { t } = useTranslation();
  const [open, setOpen] = useState<string>();
  const [adding, setAdding] = useState<FlybySectionKind>('orbit');
  const kindName = (kind: FlybySectionKind) => t(`flyby.section.${kind}`);
  const label = (index: number) => `${index + 1}. ${kindName(document.sections[index].kind)}`;

  const problems = new Map<string, string[]>();
  for (const problem of flybyProblems(document)) {
    if (SECTION_PROBLEMS.has(problem.code) && 'section' in problem && problem.section) {
      problems.set(problem.section, [...(problems.get(problem.section) ?? []), t(`flyby.edit.problem.${problem.code}`)]);
    }
  }
  const count = document.sections.length;

  return (
    <div className="scene3d-flyby-group">
      <ol className="scene3d-flyby-sections" data-testid="flyby-sections">
        {document.sections.map((section, index) => {
          const name = label(index);
          const editing = open === section.id;
          return (
            <li
              key={section.id}
              className={current === index ? 'scene3d-flyby-section-current' : undefined}
              data-testid={`flyby-section-${section.id}`}
            >
              <span className="scene3d-flyby-section-name">
                {kindName(section.kind)}
                <Typography.Text type="secondary">
                  {' · '}
                  {section.each
                    ? t('flyby.byFilter.eachOf', { name: subjectLabel(section.subject) })
                    : subjectLabel(section.subject)}
                </Typography.Text>
              </span>
              <InputNumber
                size="small"
                min={FLYBY_SECONDS_RANGE.min}
                max={FLYBY_SECONDS_RANGE.max}
                step={1}
                value={section.seconds}
                onChange={(value) => value !== null && onEdit(updateSection(document, section.id, { seconds: value }))}
                disabled={busy}
                suffix={t('flyby.secondsUnit')}
                aria-label={t('flyby.sectionSeconds', { section: kindName(section.kind) })}
                data-testid={`flyby-section-${section.id}-seconds`}
              />
              <Select
                size="small"
                value={section.speed ?? 1}
                onChange={(value) => onEdit(updateSection(document, section.id, { speed: value }))}
                disabled={busy}
                aria-label={t('flyby.sectionSpeed', { section: kindName(section.kind) })}
                data-testid={`flyby-section-${section.id}-speed`}
                options={SECTION_SPEEDS.map((value) => ({ value, label: `×${value}` }))}
              />
              <Typography.Text type="secondary" className="scene3d-flyby-section-length">
                {flybyClock(sectionLength(document, section))}
              </Typography.Text>
              <div className="scene3d-flyby-section-actions">
                <Button
                  size="small"
                  type={editing ? 'primary' : 'text'}
                  icon={<EditOutlined />}
                  disabled={busy}
                  aria-expanded={editing}
                  aria-label={t('flyby.edit.open', { section: name })}
                  onClick={() => setOpen(editing ? undefined : section.id)}
                  data-testid={`flyby-section-${section.id}-edit`}
                />
                <Button
                  size="small"
                  type="text"
                  icon={<ArrowUpOutlined />}
                  disabled={busy || index === 0}
                  aria-label={t('flyby.edit.up', { section: name })}
                  onClick={() => onEdit(moveSection(document, section.id, -1))}
                  data-testid={`flyby-section-${section.id}-up`}
                />
                <Button
                  size="small"
                  type="text"
                  icon={<ArrowDownOutlined />}
                  disabled={busy || index === count - 1}
                  aria-label={t('flyby.edit.down', { section: name })}
                  onClick={() => onEdit(moveSection(document, section.id, 1))}
                  data-testid={`flyby-section-${section.id}-down`}
                />
                <Button
                  size="small"
                  type="text"
                  icon={<CopyOutlined />}
                  disabled={busy}
                  aria-label={t('flyby.edit.duplicate', { section: name })}
                  onClick={() => onEdit(duplicateSection(document, section.id))}
                  data-testid={`flyby-section-${section.id}-duplicate`}
                />
                <Button
                  size="small"
                  type="text"
                  danger
                  icon={<DeleteOutlined />}
                  // A flyby of no sections cannot be played; the last one stays.
                  disabled={busy || count === 1}
                  aria-label={t('flyby.edit.remove', { section: name })}
                  onClick={() => onEdit(removeSection(document, section.id))}
                  data-testid={`flyby-section-${section.id}-remove`}
                />
              </div>
              {problems.has(section.id) && (
                <Typography.Text type="danger" className="scene3d-flyby-section-wide" data-testid={`flyby-section-${section.id}-trouble`}>
                  {problems.get(section.id)!.join(' ')}
                </Typography.Text>
              )}
              {editing && (
                <SectionEditor
                  document={document}
                  section={section}
                  name={name}
                  busy={busy}
                  subjectLabel={subjectLabel}
                  change={(patch) => onEdit(updateSection(document, section.id, patch))}
                />
              )}
            </li>
          );
        })}
      </ol>
      <div className="scene3d-flyby-row">
        <Select
          size="small"
          className="scene3d-flyby-grow"
          value={adding}
          onChange={setAdding}
          disabled={busy}
          aria-label={t('flyby.edit.addKind')}
          data-testid="flyby-section-add-kind"
          options={FLYBY_ADDABLE_KINDS.map((kind) => ({ value: kind, label: kindName(kind) }))}
        />
        <Button
          size="small"
          icon={<PlusOutlined />}
          disabled={busy}
          onClick={() => onEdit(addSection(document, adding))}
          data-testid="flyby-section-add"
        >
          {t('flyby.edit.add')}
        </Button>
      </div>
    </div>
  );
}

interface SectionEditorProps {
  document: FlybyDocument;
  section: FlybySection;
  name: string;
  busy: boolean;
  subjectLabel(ref: FlybySubjectRef): string;
  change(patch: FlybySectionPatch): void;
}

/**
 * Everything one section does. A number left empty, and a choice left at "as before", is a member
 * the section does not mention: the camera keeps what the section before left it.
 */
function SectionEditor({ document, section, name, busy, subjectLabel, change }: SectionEditorProps) {
  const { t } = useTranslation();
  const [turnUnit, setTurnUnit] = useState<'degrees' | 'turns'>(() =>
    section.to.headingBy !== undefined && section.to.headingBy % 90 === 0 && Math.abs(section.to.headingBy) >= 360 ? 'turns' : 'degrees',
  );
  const named = (field: string) => t('flyby.edit.fieldFor', { field, section: name });
  const id = (field: string) => `flyby-section-${section.id}-${field}`;
  const to = section.to;
  const clearanceMode = to.clearance === undefined ? BEFORE : to.clearance === null ? 'underground' : 'above';
  const perTurn = turnUnit === 'turns' ? 360 : 1;
  const subjects: { value: string; label: string }[] = [
    ...document.subjects.map((_, index) => ({ value: String(index), label: subjectLabel(index) })),
    ...(document.subjects.length > 1 ? [{ value: 'all', label: subjectLabel('all') }] : []),
  ];
  const number = (field: keyof FlybySection['to'], value: number | null) => change({ to: { [field]: value ?? undefined } });

  return (
    <fieldset className="scene3d-flyby-section-editor scene3d-flyby-section-wide" data-testid={id('editor')}>
      <legend>{t('flyby.edit.legend', { section: name })}</legend>
      <label className="scene3d-flyby-field">
        <span>{t('flyby.edit.kind')}</span>
        <Select
          size="small"
          value={section.kind}
          onChange={(kind: FlybySectionKind) => change({ kind })}
          disabled={busy}
          aria-label={named(t('flyby.edit.kind'))}
          data-testid={id('kind')}
          options={FLYBY_SECTION_KINDS.map((kind) => ({ value: kind, label: t(`flyby.section.${kind}`) }))}
        />
      </label>
      <label className="scene3d-flyby-field">
        <span>{t('flyby.edit.subject')}</span>
        <Select
          size="small"
          value={String(section.subject)}
          onChange={(value: string) => change({ subject: value === 'all' ? 'all' : Number(value) })}
          disabled={busy}
          aria-label={named(t('flyby.edit.subject'))}
          data-testid={id('subject')}
          options={subjects}
        />
      </label>
      <div className="scene3d-flyby-field scene3d-flyby-field-wide">
        <span>{t('flyby.edit.turn')}</span>
        <div className="scene3d-flyby-row">
          <InputNumber
            size="small"
            min={-FLYBY_MAX_TURN_DEGREES / perTurn}
            max={FLYBY_MAX_TURN_DEGREES / perTurn}
            step={turnUnit === 'turns' ? 0.25 : 15}
            value={to.headingBy === undefined ? null : to.headingBy / perTurn}
            placeholder={t('flyby.edit.asBefore')}
            onChange={(value) => change({ to: { headingBy: value === null ? undefined : value * perTurn } })}
            disabled={busy}
            aria-label={named(t(turnUnit === 'turns' ? 'flyby.edit.turnTurns' : 'flyby.edit.turnDegrees'))}
            data-testid={id('turn')}
          />
          <Segmented
            size="small"
            value={turnUnit}
            onChange={(value) => setTurnUnit(value as 'degrees' | 'turns')}
            disabled={busy}
            aria-label={named(t('flyby.edit.turnUnit'))}
            data-testid={id('turn-unit')}
            options={[
              { value: 'degrees', label: '°' },
              { value: 'turns', label: t('flyby.edit.turnsShort') },
            ]}
          />
        </div>
      </div>
      {to.heading !== undefined && (
        <label className="scene3d-flyby-field">
          <span>{t('flyby.edit.heading')}</span>
          <InputNumber
            size="small"
            min={-FLYBY_MAX_TURN_DEGREES}
            max={FLYBY_MAX_TURN_DEGREES}
            step={5}
            value={to.heading}
            onChange={(value) => number('heading', value)}
            disabled={busy}
            suffix="°"
            aria-label={named(t('flyby.edit.heading'))}
            data-testid={id('heading')}
          />
        </label>
      )}
      <label className="scene3d-flyby-field">
        <span>{t('flyby.edit.pitch')}</span>
        <InputNumber
          size="small"
          min={-90}
          max={60}
          step={5}
          value={to.pitch ?? null}
          placeholder={t('flyby.edit.asBefore')}
          onChange={(value) => number('pitch', value)}
          disabled={busy}
          suffix="°"
          aria-label={named(t('flyby.edit.pitch'))}
          data-testid={id('pitch')}
        />
      </label>
      <label className="scene3d-flyby-field">
        <span>{t('flyby.edit.range')}</span>
        <InputNumber
          size="small"
          min={FLYBY_MIN_RANGE}
          max={FLYBY_MAX_RANGE}
          step={0.1}
          value={to.range ?? null}
          placeholder={t('flyby.edit.asBefore')}
          onChange={(value) => number('range', value)}
          disabled={busy}
          suffix="×"
          aria-label={named(t('flyby.edit.range'))}
          data-testid={id('range')}
        />
      </label>
      {to.rangeM !== undefined && (
        <label className="scene3d-flyby-field">
          <span>{t('flyby.edit.rangeM')}</span>
          <InputNumber
            size="small"
            min={FLYBY_RANGE_M.min}
            max={FLYBY_RANGE_M.max}
            step={10}
            value={to.rangeM}
            onChange={(value) => number('rangeM', value)}
            disabled={busy}
            suffix="m"
            aria-label={named(t('flyby.edit.rangeM'))}
            data-testid={id('range-m')}
          />
        </label>
      )}
      <label className="scene3d-flyby-field">
        <span>{t('flyby.edit.aim')}</span>
        <Select
          size="small"
          value={to.aim ?? BEFORE}
          onChange={(value: FlybyAim | typeof BEFORE) => change({ to: { aim: value === BEFORE ? undefined : value } })}
          disabled={busy}
          aria-label={named(t('flyby.edit.aim'))}
          data-testid={id('aim')}
          options={[
            { value: BEFORE, label: t('flyby.edit.asBefore') },
            { value: 'centre', label: t('flyby.edit.aimCentre') },
            { value: 'top', label: t('flyby.edit.aimTop') },
            { value: 'bottom', label: t('flyby.edit.aimBottom') },
          ]}
        />
      </label>
      <label className="scene3d-flyby-field">
        <span>{t('flyby.edit.lift')}</span>
        <InputNumber
          size="small"
          min={-FLYBY_LIFT_LIMIT}
          max={FLYBY_LIFT_LIMIT}
          step={0.1}
          value={to.lift ?? null}
          placeholder={t('flyby.edit.asBefore')}
          onChange={(value) => number('lift', value)}
          disabled={busy}
          aria-label={named(t('flyby.edit.lift'))}
          data-testid={id('lift')}
        />
      </label>
      <div className="scene3d-flyby-field scene3d-flyby-field-wide">
        <span>{t('flyby.edit.clearance')}</span>
        <div className="scene3d-flyby-row">
          <Select
            size="small"
            value={clearanceMode}
            onChange={(mode: string) =>
              change({ to: { clearance: mode === BEFORE ? undefined : mode === 'underground' ? null : (to.clearance ?? 60) } })
            }
            disabled={busy}
            aria-label={named(t('flyby.edit.clearance'))}
            data-testid={id('clearance-mode')}
            options={[
              { value: BEFORE, label: t('flyby.edit.asBefore') },
              { value: 'above', label: t('flyby.edit.clearanceAbove') },
              { value: 'underground', label: t('flyby.edit.clearanceUnderground') },
            ]}
          />
          {clearanceMode === 'above' && (
            <InputNumber
              size="small"
              min={0}
              max={FLYBY_MAX_CLEARANCE}
              step={10}
              value={to.clearance ?? null}
              onChange={(value) => value !== null && change({ to: { clearance: value } })}
              disabled={busy}
              suffix="m"
              aria-label={named(t('flyby.edit.clearanceMetres'))}
              data-testid={id('clearance')}
            />
          )}
        </div>
      </div>
      <label className="scene3d-flyby-field">
        <span>{t('flyby.atCave.say')}</span>
        <Select
          size="small"
          value={section.say ?? 'none'}
          onChange={(value: FlybySay | 'none') => change({ say: value === 'none' ? undefined : value })}
          disabled={busy}
          aria-label={named(t('flyby.atCave.say'))}
          data-testid={id('say')}
          options={[
            { value: 'none', label: t('flyby.atCave.sayNone') },
            { value: 'name', label: t('flyby.atCave.sayName') },
            { value: 'nameAndSize', label: t('flyby.atCave.sayNameAndSize') },
          ]}
        />
      </label>
      <Checkbox
        checked={section.rest === true}
        onChange={(event) => change({ rest: event.target.checked })}
        disabled={busy}
        data-testid={id('rest')}
      >
        {t('flyby.edit.rest')}
      </Checkbox>
    </fieldset>
  );
}
