// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { DeleteOutlined, DownOutlined, PlusOutlined, UpOutlined } from '@ant-design/icons';
import {
  App,
  Alert,
  Button,
  Card,
  Checkbox,
  Col,
  Drawer,
  Flex,
  Form,
  Input,
  Row,
  Select,
  Space,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCaveTypes,
  useCreateTermRuleSet,
  useEntranceTypes,
  useFeatureTypes,
  useTermRuleSet,
  useUpdateTermRuleSet,
  type TermRule,
  type ImportTargetKind,
} from '../../api/hooks.ts';

interface Props {
  setId: string | null;
  creating: boolean;
  onClose: () => void;
}

const LANGUAGES = ['ro', 'en', '*'] as const;

function blankRule(index: number): TermRule {
  return {
    id: `rule-${index + 1}-${Math.random().toString(36).slice(2, 8)}`,
    name: '',
    enabled: true,
    matchMode: 'wholeWord',
    matchName: true,
    matchDescription: false,
    terms: { ro: [] },
    target: 'surfaceFeature',
    strip: 'none',
  };
}

/**
 * The rule editor.
 *
 * Order is the set's main content, so moving a rule up and down is a first-class action rather
 * than a sort key hidden in a field: when two rules claim one candidate, the earlier one wins,
 * and dragging that order is how a club tunes what its imports propose.
 */
export default function TermRuleSetEditor({ setId, creating, onClose }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const detail = useTermRuleSet(setId ?? undefined);
  const create = useCreateTermRuleSet();
  const update = useUpdateTermRuleSet();
  const caveTypes = useCaveTypes();
  const entranceTypes = useEntranceTypes();
  const featureTypes = useFeatureTypes();

  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [rules, setRules] = useState<TermRule[]>([]);
  const [error, setError] = useState<string | null>(null);

  const open = setId !== null || creating;

  useEffect(() => {
    if (!open) {
      return;
    }
    if (creating) {
      setName('');
      setDescription('');
      setRules([blankRule(0)]);
      setError(null);
      return;
    }
    if (detail.data) {
      setName(detail.data.set.name);
      setDescription(detail.data.set.description ?? '');
      setRules(detail.data.rules);
      setError(null);
    }
  }, [open, creating, detail.data]);

  const patch = (index: number, changes: Partial<TermRule>) =>
    setRules((current) => current.map((rule, i) => (i === index ? { ...rule, ...changes } : rule)));

  const move = (index: number, delta: number) =>
    setRules((current) => {
      const next = [...current];
      const target = index + delta;
      if (target < 0 || target >= next.length) {
        return current;
      }
      [next[index], next[target]] = [next[target], next[index]];
      return next;
    });

  const typeOptions = (target: ImportTargetKind) => {
    if (target === 'cave') {
      return (caveTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
    }
    if (target === 'caveEntrance') {
      return (entranceTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
    }
    return (featureTypes.data ?? []).map((type) => ({ value: type.code, label: type.name }));
  };

  const onSave = async () => {
    setError(null);
    try {
      if (creating) {
        await create.mutateAsync({ name, description: description || null, rules, copyFromId: null });
      } else if (setId) {
        await update.mutateAsync({ id: setId, body: { name, description: description || null, rules } });
      }
      message.success(t('common.saved'));
      onClose();
    } catch (e) {
      // The server checks the whole document — a pattern that cannot be compiled, a rule
      // naming a taxonomy its target does not use — and its sentences are the useful ones.
      setError(e instanceof Error ? e.message : t('common.saveFailed'));
    }
  };

  return (
    <Drawer
      open={open}
      // A CSS length, not a token: the drawer's size takes a number or a length, and a word
      // like "large" reaches the style as an invalid width and is silently ignored.
      size="min(900px, 96vw)"
      title={creating ? t('termRules.newSet') : t('termRules.edit')}
      onClose={onClose}
      destroyOnHidden
      extra={
        <Space>
          <Button onClick={onClose}>{t('common.cancel')}</Button>
          <Button
            type="primary"
            loading={create.isPending || update.isPending}
            onClick={() => void onSave()}
            data-testid="term-rules-save"
          >
            {t('common.save')}
          </Button>
        </Space>
      }
    >
      <Flex vertical gap={16}>
        {error && <Alert type="error" showIcon title={t('termRules.invalid')} description={error} />}

        <Form layout="vertical">
          <Form.Item label={t('termRules.name')} required>
            <Input value={name} maxLength={200} onChange={(e) => setName(e.target.value)} />
          </Form.Item>
          <Form.Item label={t('features.description')}>
            <Input.TextArea
              rows={2}
              value={description}
              maxLength={2000}
              onChange={(e) => setDescription(e.target.value)}
            />
          </Form.Item>
        </Form>

        <Typography.Text type="secondary">{t('termRules.orderHint')}</Typography.Text>

        {rules.map((rule, index) => (
          <Card
            key={rule.id}
            size="small"
            title={
              <Flex gap={8} align="center">
                <Typography.Text type="secondary">{index + 1}.</Typography.Text>
                <Input
                  size="small"
                  placeholder={t('termRules.ruleName')}
                  value={rule.name}
                  onChange={(e) => patch(index, { name: e.target.value })}
                  style={{ maxWidth: 260 }}
                />
                <Checkbox
                  checked={rule.enabled ?? true}
                  onChange={(e) => patch(index, { enabled: e.target.checked })}
                >
                  {t('termRules.enabled')}
                </Checkbox>
              </Flex>
            }
            extra={
              <Space.Compact>
                <Button size="small" icon={<UpOutlined />} onClick={() => move(index, -1)} />
                <Button size="small" icon={<DownOutlined />} onClick={() => move(index, 1)} />
                <Button
                  size="small"
                  danger
                  icon={<DeleteOutlined />}
                  onClick={() => setRules((current) => current.filter((_, i) => i !== index))}
                />
              </Space.Compact>
            }
          >
            <Row gutter={[12, 8]}>
              <Col xs={24} md={8}>
                <Form.Item label={t('termRules.matchMode')} style={{ marginBottom: 8 }}>
                  <Select
                    size="small"
                    value={rule.matchMode ?? 'wholeWord'}
                    onChange={(value) => patch(index, { matchMode: value })}
                    options={(['contains', 'wholeWord', 'prefix', 'regex'] as const).map((mode) => ({
                      value: mode,
                      label: t(`termRules.matchModes.${mode}`),
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item label={t('termRules.target')} style={{ marginBottom: 8 }}>
                  <Select
                    size="small"
                    value={rule.target ?? 'surfaceFeature'}
                    onChange={(value: ImportTargetKind) =>
                      // Clearing the other two codes with the target keeps the rule savable:
                      // a rule may only name the taxonomy its target uses.
                      patch(index, {
                        target: value,
                        caveTypeCode: null,
                        entranceTypeCode: null,
                        featureTypeCode: null,
                      })
                    }
                    options={(['cave', 'caveEntrance', 'surfaceFeature'] as const).map((kind) => ({
                      value: kind,
                      label: t(`vectorImport.kinds.${kind}`),
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={8}>
                <Form.Item label={t('termRules.proposedType')} style={{ marginBottom: 8 }}>
                  <Select
                    size="small"
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    value={
                      rule.target === 'cave'
                        ? (rule.caveTypeCode ?? undefined)
                        : rule.target === 'caveEntrance'
                          ? (rule.entranceTypeCode ?? undefined)
                          : (rule.featureTypeCode ?? undefined)
                    }
                    onChange={(value?: string) =>
                      patch(index, {
                        caveTypeCode: rule.target === 'cave' ? (value ?? null) : null,
                        entranceTypeCode: rule.target === 'caveEntrance' ? (value ?? null) : null,
                        featureTypeCode: rule.target === 'surfaceFeature' ? (value ?? null) : null,
                      })
                    }
                    options={typeOptions(rule.target ?? 'surfaceFeature')}
                  />
                </Form.Item>
              </Col>

              {LANGUAGES.map((language) => (
                <Col xs={24} md={8} key={language}>
                  <Form.Item
                    label={t(`termRules.languages.${language === '*' ? 'any' : language}`)}
                    style={{ marginBottom: 8 }}
                  >
                    <Select
                      size="small"
                      mode="tags"
                      value={rule.terms?.[language] ?? []}
                      onChange={(value: string[]) =>
                        patch(index, { terms: { ...rule.terms, [language]: value } })
                      }
                      placeholder={t('termRules.termsPlaceholder')}
                      open={false}
                      suffixIcon={null}
                    />
                  </Form.Item>
                </Col>
              ))}

              <Col xs={24} md={8}>
                <Form.Item label={t('termRules.strip')} style={{ marginBottom: 8 }}>
                  <Select
                    size="small"
                    value={rule.strip ?? 'none'}
                    onChange={(value) => patch(index, { strip: value })}
                    options={(['none', 'leading', 'trailing', 'anywhere'] as const).map((mode) => ({
                      value: mode,
                      label: t(`termRules.stripModes.${mode}`),
                    }))}
                  />
                </Form.Item>
              </Col>
              <Col xs={24} md={16}>
                <Space>
                  <Checkbox
                    checked={rule.matchName ?? true}
                    onChange={(e) => patch(index, { matchName: e.target.checked })}
                  >
                    {t('termRules.readName')}
                  </Checkbox>
                  <Checkbox
                    checked={rule.matchDescription ?? false}
                    onChange={(e) => patch(index, { matchDescription: e.target.checked })}
                  >
                    {t('termRules.readDescription')}
                  </Checkbox>
                </Space>
              </Col>
            </Row>
          </Card>
        ))}

        <Button
          icon={<PlusOutlined />}
          onClick={() => setRules((current) => [...current, blankRule(current.length)])}
        >
          {t('termRules.addRule')}
        </Button>
      </Flex>
    </Drawer>
  );
}
