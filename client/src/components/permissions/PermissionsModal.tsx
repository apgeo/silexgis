// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { DeleteOutlined, PlusOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { App, Alert, Button, Checkbox, Collapse, Flex, Modal, Select, Table, Tag, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useEffectiveAccess,
  useObjectAccess,
  useReplaceObjectAccess,
  useUserSearch,
  type AccessActionFlag,
  type EntityType,
  type ObjectAccessEntry,
  parseAccessActions,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import AccessExplanationList from './AccessExplanationList.tsx';
import { ACTION_ORDER, joinActions } from './accessDisplay.ts';

// Create is rejected at object scope (nothing is created "into" one row) and valid at
// subtree scope (create under this feature) — mirroring the server's scope table.
const objectActions = ACTION_ORDER.filter((flag) => flag !== 'create');
const subtreeActions = ACTION_ORDER;

interface DraftRule {
  subjectKind: 'user' | 'cavingGroup';
  subjectId: string;
  subjectName: string | null;
  effect: 'allow' | 'deny';
  scopeKind: 'object' | 'subtree';
  actions: Set<AccessActionFlag>;
}

interface PermissionsModalProps {
  /** Grantable object class; anything in the feature world is granted as 'feature'. */
  entityType: EntityType;
  entityId: string;
  open: boolean;
  onClose: () => void;
}

/**
 * Direct-rules editor for one object: rows of user/caving-group subjects, each carrying
 * an effect (allow or an explicit deny), an action set, and — for features, which contain
 * other things — a reach of this object alone or its whole subtree. Saved as a full
 * replace; requires ManagePermissions server-side.
 *
 * Beside the editor sits the explainer: what the caller may do here and which rule
 * decided each action, served ready-made by the server and rendered verbatim.
 */
export default function PermissionsModal({ entityType, entityId, open, onClose }: PermissionsModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { token } = theme.useToken();
  const { data: rules, isError } = useObjectAccess(entityType, entityId, open);
  const replaceRules = useReplaceObjectAccess(entityType, entityId);
  const { data: cavingGroups } = useCavingGroups();
  const { data: effective } = useEffectiveAccess(entityType, entityId, { explain: true, enabled: open });
  const [entries, setEntries] = useState<DraftRule[]>([]);
  const [subjectKind, setSubjectKind] = useState<'user' | 'cavingGroup'>('user');
  const [subjectId, setSubjectId] = useState<string>();
  const [effect, setEffect] = useState<'allow' | 'deny'>('allow');
  const [scopeKind, setScopeKind] = useState<'object' | 'subtree'>('object');
  const [userQuery, setUserQuery] = useState('');
  const debouncedUserQuery = useDebouncedValue(userQuery);
  const { data: users } = useUserSearch(debouncedUserQuery);

  // Only the feature world contains other objects, so only it offers subtree reach.
  const scopedToFeature = entityType === 'feature';

  useEffect(() => {
    if (open && rules) {
      setEntries(rules.map((entry: ObjectAccessEntry) => ({
        subjectKind: entry.subjectKind,
        subjectId: entry.subjectId,
        subjectName: entry.subjectName ?? null,
        effect: entry.effect,
        scopeKind: entry.scopeKind === 'subtree' ? 'subtree' : 'object',
        actions: new Set([...parseAccessActions(entry.actions)]),
      })));
    }
  }, [open, rules]);

  const hasDeny = entries.some((entry) => entry.effect === 'deny');

  const addRule = () => {
    if (!subjectId || entries.some((e) =>
      e.subjectId === subjectId && e.subjectKind === subjectKind
      && e.effect === effect && e.scopeKind === scopeKind)) {
      return;
    }
    const name = subjectKind === 'cavingGroup'
      ? cavingGroups?.find((x) => x.id === subjectId)?.name ?? null
      : users?.find((x) => x.id === subjectId)?.label ?? null;
    setEntries([...entries, {
      subjectKind,
      subjectId,
      subjectName: name,
      effect,
      scopeKind: scopedToFeature ? scopeKind : 'object',
      actions: new Set<AccessActionFlag>(['read']),
    }]);
    setSubjectId(undefined);
    setUserQuery('');
  };

  const toggleAction = (index: number, flag: AccessActionFlag, checked: boolean) => {
    setEntries(entries.map((entry, i) => {
      if (i !== index) {
        return entry;
      }
      const next = new Set(entry.actions);
      if (checked) {
        next.add(flag);
      } else {
        next.delete(flag);
      }
      return { ...entry, actions: next };
    }));
  };

  const onSave = async () => {
    try {
      await replaceRules.mutateAsync(entries
        .filter((e) => e.actions.size > 0)
        .map((e) => ({
          subjectKind: e.subjectKind,
          subjectId: e.subjectId,
          effect: e.effect,
          actions: joinActions(
            // A create bit can linger from a subtree row later switched to object reach;
            // the server would reject the whole save over it.
            e.scopeKind === 'object'
              ? new Set([...e.actions].filter((flag) => flag !== 'create'))
              : e.actions,
          ),
          scopeKind: e.scopeKind,
        })));
      message.success(t('common.saved'));
      onClose();
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  const explainer = useMemo(() => (effective?.explain ? (
    <Collapse
      ghost
      items={[{
        key: 'why',
        label: (
          <span>
            <QuestionCircleOutlined /> {t('access.whyTitle')}
          </span>
        ),
        children: <AccessExplanationList explanations={effective.explain} />,
      }]}
    />
  ) : null), [effective, t]);

  return (
    <Modal
      title={t('permissions.title')}
      open={open}
      onCancel={onClose}
      onOk={() => void onSave()}
      okButtonProps={{ disabled: isError }}
      confirmLoading={replaceRules.isPending}
      width={900}
      destroyOnHidden
    >
      {isError ? (
        <Tag color="warning">{t('permissions.notManager')}</Tag>
      ) : (
        <>
          <Flex gap={8} style={{ marginBottom: 12 }} wrap>
            <Select
              value={subjectKind}
              style={{ width: 130 }}
              onChange={(kind: 'user' | 'cavingGroup') => {
                setSubjectKind(kind);
                setSubjectId(undefined);
              }}
              options={[
                { value: 'user', label: t('permissions.user') },
                { value: 'cavingGroup', label: t('permissions.cavingGroup') },
              ]}
            />
            {subjectKind === 'cavingGroup' ? (
              <Select
                style={{ flex: 1, minWidth: 180 }}
                // CavingGroups arrive in full, so this filters client-side — unlike the user
                // picker beside it, which searches the server. Named explicitly because
                // the option values are ids: filtering the default value prop would
                // match nothing a person could type.
                showSearch
                optionFilterProp="label"
                placeholder={t('permissions.pickCavingGroup')}
                value={subjectId}
                onChange={setSubjectId}
                options={cavingGroups?.map((group) => ({ value: group.id, label: group.name }))}
              />
            ) : (
              <Select
                style={{ flex: 1, minWidth: 180 }}
                showSearch
                filterOption={false}
                placeholder={t('permissions.pickUser')}
                value={subjectId}
                onSearch={setUserQuery}
                onChange={setSubjectId}
                options={users?.map((user) => ({
                  value: user.id,
                  // The address is only present when the person shares it; the label always is.
                  label: user.email ? `${user.label} (${user.email})` : user.label,
                }))}
                notFoundContent={null}
              />
            )}
            <Select
              value={effect}
              style={{ width: 110 }}
              onChange={setEffect}
              options={[
                { value: 'allow', label: t('access.effects.allow') },
                { value: 'deny', label: <Tag color="red" style={{ marginInlineEnd: 0 }}>{t('access.effects.deny')}</Tag> },
              ]}
            />
            {scopedToFeature && (
              <Select
                value={scopeKind}
                style={{ width: 210 }}
                onChange={setScopeKind}
                options={[
                  { value: 'object', label: t('permissions.scopeObject') },
                  { value: 'subtree', label: t('permissions.scopeSubtree') },
                ]}
              />
            )}
            <Button icon={<PlusOutlined />} onClick={addRule} disabled={!subjectId}>
              {t('permissions.addRule')}
            </Button>
          </Flex>

          {hasDeny && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('permissions.denyHint')}
            />
          )}

          <Table<DraftRule>
            scroll={{ x: 'max-content' }}
            rowKey={(entry) => `${entry.subjectKind}:${entry.subjectId}:${entry.effect}:${entry.scopeKind}`}
            size="small"
            pagination={false}
            dataSource={entries}
            // A deny must be impossible to overlook next to a screen of allows.
            onRow={(entry) => (entry.effect === 'deny'
              ? { style: { background: token.colorErrorBg } }
              : {})}
            columns={[
              {
                title: t('permissions.subject'),
                key: 'subject',
                render: (_, entry) => (
                  <>
                    <Tag>{t(`permissions.${entry.subjectKind}`)}</Tag>
                    {entry.subjectName ?? entry.subjectId}
                  </>
                ),
              },
              {
                title: t('access.effect'),
                key: 'effect',
                width: 90,
                render: (_, entry) => (
                  <Tag color={entry.effect === 'deny' ? 'red' : 'green'}>
                    {t(`access.effects.${entry.effect}`)}
                  </Tag>
                ),
              },
              ...(scopedToFeature
                ? [{
                    title: t('permissions.scope'),
                    key: 'scope',
                    width: 170,
                    render: (_: unknown, entry: DraftRule, index: number) => (
                      <Select
                        size="small"
                        value={entry.scopeKind}
                        style={{ width: 160 }}
                        onChange={(next: 'object' | 'subtree') =>
                          setEntries(entries.map((e, i) => (i === index ? { ...e, scopeKind: next } : e)))}
                        options={[
                          { value: 'object', label: t('permissions.scopeObject') },
                          { value: 'subtree', label: t('permissions.scopeSubtree') },
                        ]}
                      />
                    ),
                  }]
                : []),
              // Create only ever applies to subtree reach, which only features offer.
              ...(scopedToFeature ? subtreeActions : objectActions).map((flag) => ({
                title: t(`access.actions.${flag}`),
                key: flag,
                width: 84,
                align: 'center' as const,
                render: (_: unknown, entry: DraftRule, index: number) => (
                  <Checkbox
                    checked={entry.actions.has(flag)}
                    disabled={flag === 'create' && entry.scopeKind === 'object'}
                    onChange={(e) => toggleAction(index, flag, e.target.checked)}
                  />
                ),
              })),
              {
                title: '',
                key: 'remove',
                width: 50,
                render: (_, __, index) => (
                  <Button
                    size="small"
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    onClick={() => setEntries(entries.filter((_, i) => i !== index))}
                  />
                ),
              },
            ]}
          />
        </>
      )}

      {explainer}
    </Modal>
  );
}
