// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { DeleteOutlined, PlusOutlined } from '@ant-design/icons';
import { Alert, App, Card, Flex, Input, InputNumber, Modal, Segmented, Select, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';
import { useCreateDoor } from '../../api/hooks/access.ts';
import {
  useCavePotentialWeighting,
  useCreateCavePotentialModel,
  useUpdateCavePotentialModel,
  type CavePotentialModelInfo,
  type EvidenceLayerInfo,
} from '../../api/hooks/cavePotential.ts';
import Button from '../../components/Button.tsx';
import { audienceFields, audienceFromKey, audienceKey } from '../../filters/savedFilterAudience.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import {
  JUDGMENT_CODES,
  definitionOf,
  draftOf,
  emptyDraft,
  isFilledIn,
  judgmentCode,
  judgmentOf,
  judgmentValue,
  newLayerDraft,
  withJudgment,
  type JudgmentCode,
  type LayerDraft,
  type ModelDraft,
} from './cavePotentialModel.ts';

/** What a class of a categorical layer is called, in the words its own legend uses elsewhere. */
function categoryName(t: TFunction, layer: string, code: string): string {
  return layer === 'lithology'
    ? t(`geology.lithology.${code}`, { defaultValue: code })
    : t(`terrain.landforms.${code}`, { defaultValue: code });
}

/** An evidence layer's name with the unit its values are in. */
function evidenceName(t: TFunction, info: Pick<EvidenceLayerInfo, 'code' | 'unit'>): string {
  const name = t(`cavePotential.evidence.${info.code}`, { defaultValue: info.code });
  return info.unit ? `${name} (${t(`cavePotential.units.${info.unit}`, { defaultValue: info.unit })})` : name;
}

function judgmentLabel(t: TFunction, code: JudgmentCode): string {
  if (code === '1') {
    return t('cavePotential.editor.judgment.equal');
  }
  return code.startsWith('1/')
    ? t('cavePotential.editor.judgment.less', { n: code.slice(2) })
    : t('cavePotential.editor.judgment.more', { n: code });
}

interface LayerCardProps {
  info: EvidenceLayerInfo;
  layer: LayerDraft;
  directWeights: boolean;
  onChange: (next: LayerDraft) => void;
  onRemove: () => void;
}

/** One evidence layer of the model: how its values are scored, and its weight when weights are given outright. */
function LayerCard({ info, layer, directWeights, onChange, onRemove }: LayerCardProps) {
  const { t } = useTranslation();
  const name = evidenceName(t, info);
  const score = (value: number | null, set: (next: number | null) => void, label: string) => (
    <InputNumber
      aria-label={label}
      min={0}
      max={1}
      step={0.1}
      value={value}
      onChange={(next) => set(next)}
      style={{ width: 90 }}
    />
  );

  return (
    <Card
      size="small"
      data-testid={`cave-potential-layer-${layer.layer}`}
      title={
        <Flex gap={8} align="center" wrap>
          <span>{name}</span>
          {info.usesPositions && <Tag color="gold">{t('cavePotential.page.positionsTag')}</Tag>}
        </Flex>
      }
      extra={
        <Button
          type="text"
          size="small"
          icon={<DeleteOutlined />}
          aria-label={t('cavePotential.editor.removeLayer', { name })}
          onClick={onRemove}
        />
      }
    >
      <Flex vertical gap={8}>
        <Flex gap={12} wrap align="flex-end">
          {info.transforms.length > 1 && (
            <label>
              <Typography.Text type="secondary">{t('cavePotential.editor.scoring')}</Typography.Text>
              <Select
                aria-label={t('cavePotential.editor.scoringOf', { name })}
                data-testid="cave-potential-layer-kind"
                style={{ width: 220, display: 'block' }}
                value={layer.kind}
                onChange={(kind) => onChange({ ...layer, kind })}
                options={info.transforms.map((kind) => ({
                  value: kind,
                  label: t(`cavePotential.transforms.${kind}`),
                }))}
              />
            </label>
          )}
          {directWeights && (
            <label>
              <Typography.Text type="secondary">{t('cavePotential.editor.weight')}</Typography.Text>
              <InputNumber
                aria-label={t('cavePotential.editor.weightOf', { name })}
                data-testid="cave-potential-layer-weight"
                min={0}
                step={1}
                value={layer.weight}
                onChange={(weight) => onChange({ ...layer, weight })}
                style={{ width: 110, display: 'block' }}
              />
            </label>
          )}
        </Flex>

        {layer.kind === 'fuzzyLinear' && (
          <>
            <Flex gap={12} wrap align="flex-end">
              <label>
                <Typography.Text type="secondary">{t('cavePotential.editor.low')}</Typography.Text>
                <InputNumber
                  aria-label={t('cavePotential.editor.lowOf', { name })}
                  data-testid="cave-potential-layer-low"
                  value={layer.low}
                  onChange={(low) => onChange({ ...layer, low })}
                  style={{ width: 140, display: 'block' }}
                />
              </label>
              <label>
                <Typography.Text type="secondary">{t('cavePotential.editor.high')}</Typography.Text>
                <InputNumber
                  aria-label={t('cavePotential.editor.highOf', { name })}
                  data-testid="cave-potential-layer-high"
                  value={layer.high}
                  onChange={(high) => onChange({ ...layer, high })}
                  style={{ width: 140, display: 'block' }}
                />
              </label>
            </Flex>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('cavePotential.editor.rampHint')}
            </Typography.Text>
          </>
        )}

        {layer.kind === 'classes' && (
          <Flex vertical gap={4}>
            {layer.classes.map((row, i) => {
              const last = i === layer.classes.length - 1;
              const setRow = (patch: Partial<typeof row>) =>
                onChange({
                  ...layer,
                  classes: layer.classes.map((other, k) => (k === i ? { ...other, ...patch } : other)),
                });
              return (
                // Rows have no identity but their place in the table.
                <Flex key={i} gap={8} align="center" wrap>
                  {last ? (
                    <Typography.Text style={{ width: 208 }}>{t('cavePotential.editor.classAbove')}</Typography.Text>
                  ) : (
                    <>
                      <Typography.Text style={{ width: 60 }}>{t('cavePotential.editor.classUpTo')}</Typography.Text>
                      <InputNumber
                        aria-label={t('cavePotential.editor.classBoundOf', { name, row: i + 1 })}
                        value={row.upperBound}
                        onChange={(upperBound) => setRow({ upperBound })}
                        style={{ width: 140 }}
                      />
                    </>
                  )}
                  <Typography.Text type="secondary">{t('cavePotential.editor.score')}</Typography.Text>
                  {score(row.score, (next) => setRow({ score: next }), t('cavePotential.editor.classScoreOf', { name, row: i + 1 }))}
                  {layer.classes.length > 2 && (
                    <Button
                      type="text"
                      size="small"
                      icon={<DeleteOutlined />}
                      aria-label={t('cavePotential.editor.removeClass', { row: i + 1 })}
                      onClick={() => onChange({ ...layer, classes: layer.classes.filter((_, k) => k !== i) })}
                    />
                  )}
                </Flex>
              );
            })}
            <div>
              <Button
                size="small"
                icon={<PlusOutlined />}
                onClick={() =>
                  onChange({
                    ...layer,
                    // The new row goes before the last, which stays the one with no upper bound.
                    classes: [
                      ...layer.classes.slice(0, -1),
                      { upperBound: null, score: null },
                      ...layer.classes.slice(-1),
                    ],
                  })
                }
              >
                {t('cavePotential.editor.addClass')}
              </Button>
            </div>
          </Flex>
        )}

        {layer.kind === 'categories' && (
          <>
            <Flex gap={8} wrap>
              {info.categories.map((code) => {
                const category = categoryName(t, layer.layer, code);
                return (
                  <label key={code} style={{ width: 170 }}>
                    <Typography.Text style={{ display: 'block', fontSize: 12 }}>{category}</Typography.Text>
                    {score(
                      layer.categories[code] ?? null,
                      (next) => onChange({ ...layer, categories: { ...layer.categories, [code]: next } }),
                      t('cavePotential.editor.categoryScoreOf', { name, category }),
                    )}
                  </label>
                );
              })}
            </Flex>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('cavePotential.editor.categoriesHint')}
            </Typography.Text>
          </>
        )}
      </Flex>
    </Card>
  );
}

interface Props {
  /** The model being changed, or null for a new one. */
  model: CavePotentialModelInfo | null;
  evidenceLayers: readonly EvidenceLayerInfo[];
  onClose: () => void;
}

/**
 * Writing a cave-potential model: which evidence it weighs, how each layer's values become scores,
 * and how the layers weigh against each other — by weights given outright, or by comparing the
 * layers two at a time.
 *
 * The weights the form comes to, and how consistent its comparisons are, are shown as it is typed
 * and are the server's own figures: the arithmetic lives in one place, and what is shown beside the
 * form is what a run will use. An inconsistent set of comparisons is warned about and never
 * refused, because revising them is a judgment that belongs to whoever made them.
 *
 * A model names kinds of evidence and numbers and never a place, so nothing here reads any
 * position. A model that weighs a layer made from recorded positions says what follows from that
 * for the maps computed from it.
 */
export default function CavePotentialModelEditor({ model, evidenceLayers, onClose }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { cavingGroups } = useCreateDoor('mapViews');
  const create = useCreateCavePotentialModel();
  const update = useUpdateCavePotentialModel();
  const [draft, setDraft] = useState<ModelDraft>(() => (model ? draftOf(model, evidenceLayers) : emptyDraft()));
  const [refusal, setRefusal] = useState<string | null>(null);
  // The layer chosen to be added next; it joins the model when the button beside it is pressed.
  const [adding, setAdding] = useState<string | null>(null);

  const infoOf = (code: string) => evidenceLayers.find((info) => info.code === code);
  const unused = evidenceLayers.filter((info) => !draft.layers.some((layer) => layer.layer === info.code));
  const usesPositions = draft.layers.some((layer) => infoOf(layer.layer)?.usesPositions);
  const pairwise = draft.method === 'pairwiseComparison';
  const filledIn = isFilledIn(draft);

  // Asked once the typing pauses, and only of a form with no blanks left.
  const settled = useDebouncedValue(draft, 400);
  const weightingRequest = useMemo(
    () => (isFilledIn(settled) ? { method: settled.method, definition: definitionOf(settled) } : null),
    [settled],
  );
  const weighting = useCavePotentialWeighting(weightingRequest);

  const audienceOptions = [
    { value: audienceKey({ visibility: 'private' }), label: t('cavePotential.audiences.private') },
    ...cavingGroups.map((group) => ({
      value: audienceKey({ visibility: 'cavingGroup', cavingGroupId: group.id }),
      label: t('cavePotential.audiences.cavingGroup', { name: group.name }),
    })),
    // The group a stored model names stays on offer even when it is not one of the reader's own.
    ...(draft.audience.visibility === 'cavingGroup'
      && !cavingGroups.some((group) => draft.audience.visibility === 'cavingGroup' && group.id === draft.audience.cavingGroupId)
      ? [{ value: audienceKey(draft.audience), label: t('cavePotential.audiences.someGroup') }]
      : []),
    { value: audienceKey({ visibility: 'authenticated' }), label: t('cavePotential.audiences.authenticated') },
  ];

  const save = async () => {
    const write = {
      name: draft.name.trim(),
      description: draft.description.trim() || null,
      method: draft.method,
      definition: definitionOf(draft),
      ...audienceFields(draft.audience),
    };
    setRefusal(null);
    try {
      if (model) {
        await update.mutateAsync({ id: model.id, write });
      } else {
        await create.mutateAsync(write);
      }
      message.success(t('cavePotential.page.saved'));
      onClose();
    } catch (error) {
      // The server says what is wrong with a definition in its own words, member by member; no
      // wording held here could name the row at fault.
      setRefusal(error instanceof ApiError && error.detail ? error.detail : t('common.saveFailed'));
    }
  };

  return (
    <Modal
      open
      width={880}
      title={model ? t('cavePotential.editor.titleEdit') : t('cavePotential.editor.titleNew')}
      onCancel={onClose}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      onOk={() => void save()}
      okButtonProps={{
        disabled: !filledIn || draft.name.trim() === '',
        loading: create.isPending || update.isPending,
        'data-testid': 'cave-potential-save',
      }}
      destroyOnHidden
    >
      <Flex vertical gap={12} data-testid="cave-potential-editor">
        <label>
          <Typography.Text type="secondary">{t('cavePotential.editor.name')}</Typography.Text>
          <Input
            aria-label={t('cavePotential.editor.name')}
            data-testid="cave-potential-name"
            maxLength={200}
            value={draft.name}
            onChange={(e) => setDraft({ ...draft, name: e.target.value })}
          />
        </label>
        <label>
          <Typography.Text type="secondary">{t('cavePotential.editor.description')}</Typography.Text>
          <Input.TextArea
            aria-label={t('cavePotential.editor.description')}
            maxLength={2000}
            autoSize={{ minRows: 1, maxRows: 4 }}
            value={draft.description}
            onChange={(e) => setDraft({ ...draft, description: e.target.value })}
          />
        </label>
        <Flex gap={12} wrap align="flex-end">
          <label>
            <Typography.Text type="secondary">{t('cavePotential.editor.audience')}</Typography.Text>
            <Select
              aria-label={t('cavePotential.editor.audience')}
              data-testid="cave-potential-audience"
              style={{ width: 300, display: 'block' }}
              value={audienceKey(draft.audience)}
              onChange={(key: string) => setDraft({ ...draft, audience: audienceFromKey(key) })}
              options={audienceOptions}
            />
          </label>
          <div>
            <Typography.Text type="secondary" style={{ display: 'block' }}>
              {t('cavePotential.editor.method')}
            </Typography.Text>
            <Segmented
              aria-label={t('cavePotential.editor.method')}
              data-testid="cave-potential-method"
              value={draft.method}
              onChange={(method) => setDraft({ ...draft, method })}
              options={[
                { value: 'directWeights' as const, label: t('cavePotential.methods.directWeights') },
                { value: 'pairwiseComparison' as const, label: t('cavePotential.methods.pairwiseComparison') },
              ]}
            />
          </div>
        </Flex>

        <Typography.Text strong>{t('cavePotential.editor.layers')}</Typography.Text>
        {draft.layers.map((layer) => {
          const info = infoOf(layer.layer);
          return info === undefined ? null : (
            <LayerCard
              key={layer.layer}
              info={info}
              layer={layer}
              directWeights={!pairwise}
              onChange={(next) =>
                setDraft({ ...draft, layers: draft.layers.map((other) => (other.layer === next.layer ? next : other)) })
              }
              onRemove={() =>
                setDraft({ ...draft, layers: draft.layers.filter((other) => other.layer !== layer.layer) })
              }
            />
          );
        })}
        {unused.length > 0 && (
          <Flex gap={8} wrap>
            <Select
              aria-label={t('cavePotential.editor.addLayer')}
              data-testid="cave-potential-add-layer"
              style={{ width: 360 }}
              virtual={false}
              placeholder={t('cavePotential.editor.addLayer')}
              value={unused.some((info) => info.code === adding) ? adding : null}
              onChange={(code: string | null) => setAdding(code)}
              options={unused.map((info) => ({ value: info.code, label: evidenceName(t, info) }))}
            />
            <Button
              icon={<PlusOutlined />}
              data-testid="cave-potential-add-layer-confirm"
              disabled={!unused.some((info) => info.code === adding)}
              onClick={() => {
                const info = adding === null ? undefined : infoOf(adding);
                if (info) {
                  setDraft({ ...draft, layers: [...draft.layers, newLayerDraft(info)] });
                  setAdding(null);
                }
              }}
            >
              {t('cavePotential.editor.addLayerConfirm')}
            </Button>
          </Flex>
        )}

        {usesPositions && (
          <Alert type="warning" showIcon data-testid="cave-potential-positions-notice" title={t('cavePotential.editor.positionsNotice')} />
        )}

        {pairwise && draft.layers.length > 1 && (
          <>
            <Typography.Text strong>{t('cavePotential.editor.comparisons')}</Typography.Text>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              {t('cavePotential.editor.comparisonsHint')}
            </Typography.Text>
            <Flex vertical gap={4} data-testid="cave-potential-comparisons">
              {draft.layers.slice(0, -1).flatMap((first, i) =>
                draft.layers.slice(i + 1).map((second) => {
                  const firstName = t(`cavePotential.evidence.${first.layer}`, { defaultValue: first.layer });
                  const secondName = t(`cavePotential.evidence.${second.layer}`, { defaultValue: second.layer });
                  const label = t('cavePotential.editor.compare', { first: firstName, second: secondName });
                  return (
                    <Flex key={`${first.layer}|${second.layer}`} gap={8} align="center" wrap>
                      <Typography.Text style={{ width: 360 }}>{label}</Typography.Text>
                      <Select
                        aria-label={label}
                        data-testid={`cave-potential-judgment-${first.layer}-${second.layer}`}
                        style={{ width: 260 }}
                        // The whole scale is in the list at once: it is seventeen steps, and a
                        // list that draws only the rows in view loses the steps above "equal"
                        // to anybody walking it from the keyboard.
                        virtual={false}
                        value={judgmentCode(judgmentOf(draft.judgments, first.layer, second.layer))}
                        onChange={(code: JudgmentCode) =>
                          setDraft({
                            ...draft,
                            judgments: withJudgment(draft.judgments, first.layer, second.layer, judgmentValue(code)),
                          })
                        }
                        options={JUDGMENT_CODES.map((code) => ({ value: code, label: judgmentLabel(t, code) }))}
                      />
                    </Flex>
                  );
                }),
              )}
            </Flex>
          </>
        )}

        <Typography.Text strong>{t('cavePotential.editor.weights')}</Typography.Text>
        {!filledIn && (
          <Typography.Text type="secondary" data-testid="cave-potential-unfinished">
            {t('cavePotential.editor.unfinished')}
          </Typography.Text>
        )}
        {filledIn && weighting.data && (
          <Flex vertical gap={8} data-testid="cave-potential-weighting">
            <Flex gap={8} wrap>
              {weighting.data.weights.map((weight) => (
                <Tag key={weight.layer} data-testid={`cave-potential-weight-${weight.layer}`}>
                  {t(`cavePotential.evidence.${weight.layer}`, { defaultValue: weight.layer })}
                  {': '}
                  {(weight.weight * 100).toFixed(1)}
                  {' %'}
                </Tag>
              ))}
            </Flex>
            {weighting.data.consistency && (
              <Alert
                type={weighting.data.consistency.acceptable ? 'success' : 'warning'}
                showIcon
                data-testid="cave-potential-consistency"
                title={t('cavePotential.editor.consistency', { ratio: weighting.data.consistency.ratio.toFixed(3) })}
                description={
                  weighting.data.consistency.acceptable
                    ? t('cavePotential.editor.consistencyFine')
                    : t('cavePotential.editor.consistencyWarning')
                }
              />
            )}
          </Flex>
        )}
        {filledIn && weighting.isError && (
          <Alert
            type="error"
            showIcon
            title={
              weighting.error instanceof ApiError && weighting.error.detail
                ? weighting.error.detail
                : t('cavePotential.editor.weightsFailed')
            }
          />
        )}

        {refusal && <Alert type="error" showIcon data-testid="cave-potential-refusal" title={refusal} />}
      </Flex>
    </Modal>
  );
}
