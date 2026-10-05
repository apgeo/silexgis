// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { DeleteOutlined, PlusOutlined, ReloadOutlined, ShareAltOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Checkbox,
  Flex,
  Modal,
  Popconfirm,
  Select,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useApplyExpeditionSharing,
  useCavingGroups,
  useExpeditionSharing,
  useReapplyExpeditionSharing,
  useUserSearch,
  useWithdrawExpeditionSharing,
  type AccessActionFlag,
  type ExpeditionSharedRule,
  type ExpeditionShareEntry,
} from '../../api/hooks.ts';
import { ACTION_ORDER, actionSetLabels, joinActions } from '../../components/permissions/accessDisplay.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

/**
 * The rights a camp may share down to its trips.
 *
 * Not *create*: a rule written onto one trip grants nothing to be created into, and the server
 * refuses it at object reach. Not *exact location*: the cascade refuses that flag outright — it
 * is inert on a trip, and offering it here would suggest a camp can hand out cave positions.
 */
const SHAREABLE: readonly AccessActionFlag[] = ACTION_ORDER.filter(
  (flag) => flag !== 'create' && flag !== 'viewExactLocation',
);

/** A rule being composed, before the act that writes it onto every member trip. */
interface Draft {
  subjectKind: 'user' | 'cavingGroup';
  subjectId: string;
  subjectName: string | null;
  effect: 'allow' | 'deny';
  actions: Set<AccessActionFlag>;
}

interface Props {
  expeditionId: string;
  open: boolean;
  onClose: () => void;
}

/**
 * Sharing a camp with somebody: what it grants on the trips the camp gathers, and the three acts
 * that change it.
 *
 * The rules do not sit on the camp. Sharing writes one object-scoped rule onto each member trip,
 * marked with the camp that wrote it, so the camp's organiser can hand a partner club the camp's
 * whole fortnight in one act and each trip still answers for itself. That shape sets the three
 * things this dialog has to say plainly: a trip that joins later is *not* covered until somebody
 * re-applies the sharing; the rules can be added to but a subject is only ever taken off by
 * withdrawing everything and applying the rest again; and the act needs the right to manage
 * permissions on *every* member trip, so a camp gathering three clubs' trips shares nothing until
 * every trip's owner has delegated — the server refuses the whole act and says how many trips
 * refused it, never which.
 */
export default function ExpeditionSharingModal({ expeditionId, open, onClose }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: sharing, isError } = useExpeditionSharing(expeditionId, open);
  const apply = useApplyExpeditionSharing(expeditionId);
  const reapply = useReapplyExpeditionSharing(expeditionId);
  const withdraw = useWithdrawExpeditionSharing(expeditionId);
  const { data: cavingGroups } = useCavingGroups();

  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [subjectKind, setSubjectKind] = useState<'user' | 'cavingGroup'>('cavingGroup');
  const [subjectId, setSubjectId] = useState<string>();
  const [effect, setEffect] = useState<'allow' | 'deny'>('allow');
  const [userQuery, setUserQuery] = useState('');
  const debouncedUserQuery = useDebouncedValue(userQuery);
  const { data: users } = useUserSearch(debouncedUserQuery);

  const memberTrips = sharing?.memberTrips ?? 0;
  const rules = sharing?.rules ?? [];

  const addDraft = () => {
    if (!subjectId || drafts.some((d) => d.subjectId === subjectId && d.subjectKind === subjectKind && d.effect === effect)) {
      return;
    }
    const name = subjectKind === 'cavingGroup'
      ? cavingGroups?.find((x) => x.id === subjectId)?.name ?? null
      : users?.find((x) => x.id === subjectId)?.label ?? null;
    setDrafts([...drafts, {
      subjectKind,
      subjectId,
      subjectName: name,
      effect,
      actions: new Set<AccessActionFlag>(['read']),
    }]);
    setSubjectId(undefined);
    setUserQuery('');
  };

  const toggleAction = (index: number, flag: AccessActionFlag, checked: boolean) => {
    setDrafts(drafts.map((draft, i) => {
      if (i !== index) {
        return draft;
      }
      const next = new Set(draft.actions);
      if (checked) {
        next.add(flag);
      } else {
        next.delete(flag);
      }
      return { ...draft, actions: next };
    }));
  };

  /**
   * The server's refusals, in words that say what to do about them. The incomplete refusal
   * carries a count and nothing else, by design: a member trip the caller cannot see must not
   * be disclosed by the refusal that mentions it.
   */
  const refusal = (error: unknown): string => {
    if (!(error instanceof ApiError)) {
      return t('common.saveFailed');
    }
    switch (error.code) {
      case 'access_cascade.incomplete': {
        const count = error.problem?.refusedTripCount;
        return t('expeditions.sharing.incomplete', { count: typeof count === 'number' ? count : 0 });
      }
      case 'expedition_sharing.no_trips':
        return t('expeditions.sharing.noTrips');
      case 'expedition_sharing.nothing_shared':
        return t('expeditions.sharing.nothingShared');
      default:
        return t('common.saveFailed');
    }
  };

  const onApply = async () => {
    const entries: ExpeditionShareEntry[] = drafts
      .filter((d) => d.actions.size > 0)
      .map((d) => ({
        subjectKind: d.subjectKind,
        subjectId: d.subjectId,
        effect: d.effect,
        actions: joinActions(d.actions),
      }));
    if (entries.length === 0) {
      return;
    }
    try {
      await apply.mutateAsync(entries);
      setDrafts([]);
      message.success(t('expeditions.sharing.applied'));
    } catch (error) {
      message.error(refusal(error));
    }
  };

  const onReapply = async () => {
    try {
      await reapply.mutateAsync();
      message.success(t('expeditions.sharing.reapplied'));
    } catch (error) {
      message.error(refusal(error));
    }
  };

  const onWithdraw = async () => {
    try {
      await withdraw.mutateAsync();
      message.success(t('expeditions.sharing.withdrawn'));
    } catch (error) {
      message.error(refusal(error));
    }
  };

  const busy = apply.isPending || reapply.isPending || withdraw.isPending;
  // Fewer trips carrying a rule than the camp has means trips joined since the sharing was
  // last applied — the one state this dialog exists to make visible.
  const behind = rules.some((rule) => rule.trips < memberTrips);

  return (
    <Modal
      title={t('expeditions.sharing.title')}
      open={open}
      onCancel={onClose}
      footer={null}
      width={860}
      destroyOnHidden
    >
      {isError ? (
        <Tag color="warning">{t('expeditions.sharing.notAllowed')}</Tag>
      ) : (
        <>
          <Typography.Paragraph type="secondary">{t('expeditions.sharing.intro')}</Typography.Paragraph>
          <Typography.Paragraph data-testid="expedition-sharing-trips">
            {t('expeditions.sharing.memberTrips', { count: memberTrips })}
          </Typography.Paragraph>

          {behind && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('expeditions.sharing.behindHint')}
              data-testid="expedition-sharing-behind"
            />
          )}

          <Table<ExpeditionSharedRule>
            scroll={{ x: 'max-content' }}
            rowKey={(rule) => `${rule.subjectKind}:${rule.subjectId}:${rule.effect}:${rule.actions}`}
            size="small"
            pagination={false}
            dataSource={rules}
            locale={{ emptyText: t('expeditions.sharing.empty') }}
            data-testid="expedition-sharing-rules"
            columns={[
              {
                title: t('permissions.subject'),
                key: 'subject',
                render: (_, rule) => (
                  <>
                    <Tag>{t(`permissions.${rule.subjectKind}`)}</Tag>
                    {rule.subjectName ?? rule.subjectId}
                  </>
                ),
              },
              {
                title: t('access.effect'),
                key: 'effect',
                width: 90,
                render: (_, rule) => (
                  <Tag color={rule.effect === 'deny' ? 'red' : 'green'}>
                    {t(`access.effects.${rule.effect}`)}
                  </Tag>
                ),
              },
              {
                title: t('expeditions.sharing.actions'),
                key: 'actions',
                render: (_, rule) => actionSetLabels(t, rule.actions).join(', '),
              },
              {
                title: t('expeditions.sharing.coverage'),
                key: 'coverage',
                width: 160,
                render: (_, rule) => (
                  <span data-testid="expedition-sharing-coverage">
                    {t('expeditions.sharing.covered', { trips: rule.trips, total: memberTrips })}
                    {rule.trips < memberTrips && (
                      <Tag color="warning" style={{ marginInlineStart: 8 }}>
                        {t('expeditions.sharing.behind')}
                      </Tag>
                    )}
                  </span>
                ),
              },
            ]}
          />

          <Flex gap={8} wrap style={{ marginTop: 12, marginBottom: 12 }}>
            {/* Re-applying is the act that covers trips which joined since: automatic coverage
                would be a grant nobody performed. Withdrawing takes back exactly what the camp
                wrote, and leaves a rule of the same shape authored on a trip's own tab alone. */}
            <Button
              icon={<ReloadOutlined />}
              loading={reapply.isPending}
              disabled={busy || rules.length === 0}
              onClick={() => void onReapply()}
              data-testid="expedition-sharing-reapply"
            >
              {t('expeditions.sharing.reapply')}
            </Button>
            <Popconfirm
              title={t('expeditions.sharing.withdrawConfirm')}
              onConfirm={() => void onWithdraw()}
              okButtonProps={{ danger: true }}
            >
              <Button
                danger
                icon={<DeleteOutlined />}
                loading={withdraw.isPending}
                disabled={busy || rules.length === 0}
                data-testid="expedition-sharing-withdraw"
              >
                {t('expeditions.sharing.withdraw')}
              </Button>
            </Popconfirm>
          </Flex>

          <Typography.Title level={5} style={{ marginTop: 16 }}>
            {t('expeditions.sharing.addTitle')}
          </Typography.Title>
          <Flex gap={8} style={{ marginBottom: 12 }} wrap>
            <Select
              value={subjectKind}
              style={{ width: 130 }}
              onChange={(kind: 'user' | 'cavingGroup') => {
                setSubjectKind(kind);
                setSubjectId(undefined);
              }}
              options={[
                { value: 'cavingGroup', label: t('permissions.cavingGroup') },
                { value: 'user', label: t('permissions.user') },
              ]}
            />
            {subjectKind === 'cavingGroup' ? (
              <Select
                style={{ flex: 1, minWidth: 180 }}
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
            <Button
              icon={<PlusOutlined />}
              onClick={addDraft}
              disabled={!subjectId}
              data-testid="expedition-sharing-add"
            >
              {t('permissions.addRule')}
            </Button>
          </Flex>

          {drafts.length > 0 && (
            <>
              <Table<Draft>
                scroll={{ x: 'max-content' }}
                rowKey={(draft) => `${draft.subjectKind}:${draft.subjectId}:${draft.effect}`}
                size="small"
                pagination={false}
                dataSource={drafts}
                data-testid="expedition-sharing-drafts"
                columns={[
                  {
                    title: t('permissions.subject'),
                    key: 'subject',
                    render: (_, draft) => (
                      <>
                        <Tag>{t(`permissions.${draft.subjectKind}`)}</Tag>
                        {draft.subjectName ?? draft.subjectId}
                      </>
                    ),
                  },
                  {
                    title: t('access.effect'),
                    key: 'effect',
                    width: 90,
                    render: (_, draft) => (
                      <Tag color={draft.effect === 'deny' ? 'red' : 'green'}>
                        {t(`access.effects.${draft.effect}`)}
                      </Tag>
                    ),
                  },
                  ...SHAREABLE.map((flag) => ({
                    title: t(`access.actions.${flag}`),
                    key: flag,
                    width: 84,
                    align: 'center' as const,
                    render: (_: unknown, draft: Draft, index: number) => (
                      <Checkbox
                        checked={draft.actions.has(flag)}
                        onChange={(e) => toggleAction(index, flag, e.target.checked)}
                      />
                    ),
                  })),
                  {
                    title: '',
                    key: 'remove',
                    width: 50,
                    render: (_, _draft, index) => (
                      <Button
                        size="small"
                        type="text"
                        danger
                        icon={<DeleteOutlined />}
                        onClick={() => setDrafts(drafts.filter((_, i) => i !== index))}
                      />
                    ),
                  },
                ]}
              />
              <Flex gap={8} align="center" style={{ marginTop: 12 }}>
                <Button
                  type="primary"
                  icon={<ShareAltOutlined />}
                  loading={apply.isPending}
                  disabled={busy}
                  onClick={() => void onApply()}
                  data-testid="expedition-sharing-apply"
                >
                  {t('expeditions.sharing.apply')}
                </Button>
                <Typography.Text type="secondary">{t('expeditions.sharing.applyHint')}</Typography.Text>
              </Flex>
            </>
          )}
        </>
      )}
    </Modal>
  );
}
