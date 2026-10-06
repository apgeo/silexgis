// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo, useState } from 'react';
import { DeleteOutlined, PlusOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { App, Alert, Button, Checkbox, Collapse, Flex, Modal, Select, Table, Tag, theme } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useEffectiveAccess,
  useMember,
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
import ViaExpeditionTag from './ViaExpeditionTag.tsx';
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
  /**
   * Set when the rule was written by a camp being shared down to the trips inside it. Such a
   * rule is anchored on this object but is not one of its own: the save below replaces only the
   * rules written here, so a camp's rule offered as editable would be a row somebody deletes,
   * saves, and finds back where it was. It is shown — hiding it would leave a reader wondering
   * who else can read this — and it is shown as not theirs to change.
   */
  grantedViaExpeditionId: string | null;
  /**
   * Set on the one rule this dialog drafted by itself, for the account it was opened about, and
   * on nothing the server sent or a person added. It never travels: it is what the line above
   * the table and the mark on the row are drawn from, so that a row nobody here typed is never
   * mistaken for one that is already in force.
   */
  proposed: boolean;
}

interface PermissionsModalProps {
  /** Grantable object class; anything in the feature world is granted as 'feature'. */
  entityType: EntityType;
  entityId: string;
  open: boolean;
  onClose: () => void;
  /**
   * The account this dialog was opened about, when it was opened from an address that names
   * one — a message saying that somebody cannot open this object links here with that person in
   * it. The dialog then opens with Read on this object alone drafted for them, so the reader
   * does not have to find the person by hand.
   *
   * A convenience and nothing more. The draft is a row like any other, saved by the same button
   * through the same route, and refused there if it is more than its author may hand out; an id
   * that names nobody the reader can look up drafts nothing and is passed over in silence, so
   * the address cannot be used to ask whether an account exists.
   */
  grantTo?: string | null;
}

/** An account's id as an address carries it; anything else names nobody and is not looked up. */
const ACCOUNT_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * The rules with Read on this object alone drafted for one account — the smallest thing that
 * lets them open it. Never more than Read: seeing exactly where a protected cave lies, or
 * changing anything, is a separate decision a person makes by ticking it.
 *
 * Answers the same list when a rule of this object's own already gives that account Read, so
 * running it twice drafts once, and following the link after somebody else has acted on it
 * drafts nothing.
 */
function withReadProposedFor(entries: DraftRule[], userId: string, label: string): DraftRule[] {
  // Only a rule this object owns counts: one a camp wrote is not this dialog's to rely on, and
  // is withdrawn from somewhere else without anybody here being asked.
  const theirs = (entry: DraftRule) =>
    entry.grantedViaExpeditionId === null
    && entry.subjectKind === 'user' && entry.subjectId === userId && entry.effect === 'allow';
  if (entries.some((entry) => theirs(entry) && entry.actions.has('read'))) {
    return entries;
  }
  // A subject has one rule per effect and reach, and a second is refused as a duplicate — so
  // where theirs is already here without Read, Read is proposed on it rather than beside it.
  const existing = entries.findIndex((entry) => theirs(entry) && entry.scopeKind === 'object');
  if (existing >= 0) {
    return entries.map((entry, index) => (index === existing
      ? { ...entry, actions: new Set<AccessActionFlag>([...entry.actions, 'read']), proposed: true }
      : entry));
  }
  // First, directly under the line that explains it, rather than below however many rules the
  // object already has.
  return [{
    subjectKind: 'user',
    subjectId: userId,
    subjectName: label,
    effect: 'allow',
    scopeKind: 'object',
    actions: new Set<AccessActionFlag>(['read']),
    grantedViaExpeditionId: null,
    proposed: true,
  }, ...entries];
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
export default function PermissionsModal({
  entityType,
  entityId,
  open,
  onClose,
  grantTo,
}: PermissionsModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { token } = theme.useToken();
  const { data: rules, isError } = useObjectAccess(entityType, entityId, open);
  const replaceRules = useReplaceObjectAccess(entityType, entityId);
  const { data: cavingGroups } = useCavingGroups();
  const { data: effective } = useEffectiveAccess(entityType, entityId, { explain: true, enabled: open });
  // Who the dialog was opened about, as the reader's own lookup of them answers — the same read
  // the members directory makes, so the name drawn here is one this reader may be shown. An id
  // that answers nothing leaves this empty for good, and nothing below then mentions it.
  const { data: grantee } = useMember(open && grantTo && ACCOUNT_ID.test(grantTo) ? grantTo : '');
  const granteeId = grantee?.id;
  const granteeLabel = grantee?.label;
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
        grantedViaExpeditionId: entry.grantedViaExpeditionId ?? null,
        proposed: false,
      })));
    }
  }, [open, rules]);

  // Drafts Read for the account the dialog was opened about, once both the rules and the
  // account are known. Declared after the effect above so that, when the rules arrive or
  // change, the draft is made over the list that effect has just put in place; when it is the
  // account that arrives second, it is added to whatever is on screen and nothing a person has
  // already edited is thrown away. Keyed on the id and the name rather than on the fetched
  // record, so a background refresh of an unchanged profile does not put back a row somebody
  // has just removed.
  useEffect(() => {
    if (open && rules && granteeId !== undefined && granteeLabel !== undefined) {
      setEntries((current) => withReadProposedFor(current, granteeId, granteeLabel));
    }
  }, [open, rules, granteeId, granteeLabel]);

  const hasDeny = entries.some((entry) => entry.effect === 'deny');
  const hasInherited = entries.some((entry) => entry.grantedViaExpeditionId !== null);
  // Read off the rows rather than remembered: once the drafted row is removed there is nothing
  // proposed, and a line still saying so would be describing a table that is not there.
  const proposal = entries.find((entry) => entry.proposed);
  // Somebody else got there first — the same message goes to several people — or the account
  // held a rule with Read here all along. Said in words, because otherwise the address opens an
  // ordinary dialog and its reader is left to work out why nothing in it looks any different.
  // Asked of what the server holds rather than of the rows on screen, so that it is a statement
  // about rules in force and never about something typed a moment ago.
  const alreadyReads = granteeId !== undefined && (rules ?? []).some((rule: ObjectAccessEntry) =>
    (rule.grantedViaExpeditionId ?? null) === null
    && rule.subjectKind === 'user' && rule.subjectId === granteeId
    && rule.effect === 'allow' && parseAccessActions(rule.actions).has('read'));

  const addRule = () => {
    // Only a rule this object owns blocks another like it. A rule a camp wrote is shown here but
    // is not this object's to edit, so counting it as a duplicate would leave a subject the camp
    // already named with no way to be given a right of the trip's own — the button would do
    // nothing and there would be nothing on screen saying why.
    if (!subjectId || entries.some((e) =>
      e.grantedViaExpeditionId === null
      && e.subjectId === subjectId && e.subjectKind === subjectKind
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
      grantedViaExpeditionId: null,
      proposed: false,
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
        // A rule a camp wrote is not this object's to replace: the server keeps it whatever
        // this save says, so sending it back would only invite it to be sent as something else.
        .filter((e) => e.grantedViaExpeditionId === null)
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

          {/* Why a row nobody here typed is in the table, and that it is not in force. Drawn
              only while that row is there; an id that named nobody draws neither line. */}
          {proposal !== undefined && !alreadyReads && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              data-testid="permissions-proposed"
              title={t('permissions.proposedHint', { name: proposal.subjectName ?? granteeLabel ?? '' })}
            />
          )}
          {alreadyReads && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              data-testid="permissions-proposed-already"
              title={t('permissions.proposedAlready', { name: granteeLabel ?? '' })}
            />
          )}

          {hasInherited && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('permissions.viaExpeditionHint')}
            />
          )}

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
            rowKey={(entry) =>
              `${entry.subjectKind}:${entry.subjectId}:${entry.effect}:${entry.scopeKind}`
              + `:${entry.grantedViaExpeditionId ?? ''}`}
            size="small"
            pagination={false}
            dataSource={entries}
            // A deny must be impossible to overlook next to a screen of allows, and so must the
            // one row that is here because of an address rather than because somebody added it.
            onRow={(entry) => {
              if (entry.effect === 'deny') {
                return { style: { background: token.colorErrorBg } };
              }
              return entry.proposed ? { style: { background: token.colorInfoBg } } : {};
            }}
            columns={[
              {
                title: t('permissions.subject'),
                key: 'subject',
                render: (_, entry) => (
                  <>
                    <Tag>{t(`permissions.${entry.subjectKind}`)}</Tag>
                    {entry.subjectName ?? entry.subjectId}
                    {entry.grantedViaExpeditionId !== null && (
                      <ViaExpeditionTag expeditionId={entry.grantedViaExpeditionId} />
                    )}
                    {entry.proposed && (
                      <Tag color="blue" style={{ marginInlineStart: 8, marginInlineEnd: 0 }}>
                        {t('permissions.proposed')}
                      </Tag>
                    )}
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
                    disabled={
                      (flag === 'create' && entry.scopeKind === 'object')
                      || entry.grantedViaExpeditionId !== null
                    }
                    onChange={(e) => toggleAction(index, flag, e.target.checked)}
                  />
                ),
              })),
              {
                title: '',
                key: 'remove',
                width: 50,
                render: (_, entry, index) => (
                  <Button
                    size="small"
                    type="text"
                    danger
                    icon={<DeleteOutlined />}
                    disabled={entry.grantedViaExpeditionId !== null}
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
