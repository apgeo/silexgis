// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { App, Alert, Flex, Popconfirm, Select, Table, Tag, Typography } from 'antd';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import {
  useCan,
  useCavers,
  useLinkPhoneUser,
  usePhoneUsers,
  useUnlinkPhoneUser,
  type PhoneUser,
  type PhoneUserLinkSource,
} from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import Button from '../../components/Button.tsx';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

const PAGE_SIZE = 25;

/** A phone user is a 36-character identifier; the head of it is enough to tell two apart. */
function shortAccount(deviceUserId: string) {
  return deviceUserId.slice(0, 8);
}

/** What the phone sent for the person, as one line: the name they typed, else the login. */
function phoneUserLabel(user: PhoneUser) {
  const name = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
  return name || user.username || null;
}

const SOURCE_KEY: Record<PhoneUserLinkSource, string> = {
  device: 'phoneUsers.sourceDevice',
  archiveImport: 'phoneUsers.sourceArchiveImport',
  administrator: 'phoneUsers.sourceAdministrator',
};

/**
 * The roster search for one row.
 *
 * Opens empty unless somebody has already said who the phone user is, and proposes nobody: a login
 * on a phone is not evidence about which caver typed it, and a guess that looked like an answer
 * would be accepted by everyone who did not stop to check.
 */
function CaverPicker({
  user,
  busy,
  onChoose,
}: {
  user: PhoneUser;
  busy: boolean;
  onChoose: (caverId: string) => void;
}) {
  const { t } = useTranslation();
  const controlSize = useCoarsePointer() ? 'middle' : 'small';
  const [search, setSearch] = useState('');
  const debounced = useDebouncedValue(search);
  const candidates = useCavers(debounced || undefined);

  const found = (candidates.data ?? []).map((caver) => ({ value: caver.id, label: caver.name }));
  // The person already linked, so a row shows a name rather than an identifier when the searched
  // page does not happen to carry them.
  const options =
    user.caverId && !found.some((option) => option.value === user.caverId)
      ? [{ value: user.caverId, label: user.caverName ?? t('phoneUsers.unnamedCaver') }, ...found]
      : found;

  return (
    <Select
      showSearch
      // Searched on the server, so the box is not narrowed twice against a page it already narrowed.
      filterOption={false}
      size={controlSize}
      style={{ minWidth: 220, width: '100%' }}
      loading={candidates.isFetching || busy}
      disabled={busy}
      placeholder={t('phoneUsers.chooseCaver')}
      value={user.caverId ?? undefined}
      onSearch={setSearch}
      onChange={(caverId: string) => onChoose(caverId)}
      options={options}
      data-testid={`phone-user-pick-${shortAccount(user.deviceUserId)}`}
    />
  );
}

/**
 * Everyone who has used a phone with this installation, and which caver each one is.
 *
 * A phone user is never made into a caver or an account on its own; somebody who keeps the roster
 * says who they are here, or the phone or an archive import said so earlier. The list is the
 * server's order — people nobody has mapped first — so the work that is waiting is at the top.
 */
export default function PhoneUsersPage() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const [page, setPage] = useState(1);
  const users = usePhoneUsers(page, PAGE_SIZE);
  const link = useLinkPhoneUser();
  const unlink = useUnlinkPhoneUser();
  // Mapping is a roster edit. Reading the list is open to whoever may read people, so an account
  // without the write right sees who is mapped to whom and no controls.
  const canMap = useCan('cavers', 'write');
  const [working, setWorking] = useState<string | null>(null);

  const refusal = (error: unknown) => {
    if (error instanceof ApiError && error.code === 'sync.caver_not_found') {
      return t('phoneUsers.errors.caverNotFound');
    }
    if (error instanceof ApiError && error.code === 'sync.phone_user_not_found') {
      return t('phoneUsers.errors.userNotFound');
    }
    return t('common.saveFailed');
  };

  const choose = async (user: PhoneUser, caverId: string) => {
    if (caverId === user.caverId) {
      return;
    }
    setWorking(user.deviceUserId);
    try {
      await link.mutateAsync({ deviceUserId: user.deviceUserId, caverId });
      message.success(t('phoneUsers.linked'));
    } catch (error) {
      message.error(refusal(error));
    } finally {
      setWorking(null);
    }
  };

  const remove = async (user: PhoneUser) => {
    setWorking(user.deviceUserId);
    try {
      await unlink.mutateAsync(user.deviceUserId);
      message.success(t('phoneUsers.unlinked'));
    } catch (error) {
      message.error(refusal(error));
    } finally {
      setWorking(null);
    }
  };

  const when = (value: string) => new Date(value).toLocaleString(i18n.language);
  const rows = users.data?.items ?? [];
  // Unmapped people are listed first, so the first row of the first page says whether any wait.
  const anyUnmapped = page === 1 && rows.length > 0 && !rows[0].caverId;

  if (users.isError) {
    const forbidden = users.error instanceof ApiError && users.error.status === 403;
    return (
      <div style={{ padding: 24, maxWidth: 1000 }}>
        <Alert
          type="error"
          showIcon
          data-testid="phone-users-error"
          title={forbidden ? t('phoneUsers.forbidden') : t('phoneUsers.loadFailed')}
        />
      </div>
    );
  }

  return (
    <div style={{ padding: 24, maxWidth: 1100 }}>
      <Flex justify="space-between" align="center" wrap gap={12} style={{ marginBottom: 8 }}>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('phoneUsers.title')}
        </Typography.Title>
        <Link to="/cavers">{t('phoneUsers.backToCavers')}</Link>
      </Flex>
      <Typography.Paragraph type="secondary">{t('phoneUsers.intro')}</Typography.Paragraph>

      {anyUnmapped && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          data-testid="phone-users-unmapped"
          title={t('phoneUsers.unmappedTitle')}
          description={t('phoneUsers.unmappedBody')}
        />
      )}

      <Table<PhoneUser>
        scroll={{ x: 'max-content' }}
        rowKey="deviceUserId"
        size="middle"
        loading={users.isFetching && !users.data}
        dataSource={rows}
        locale={{ emptyText: t('phoneUsers.empty') }}
        pagination={{
          current: page,
          pageSize: PAGE_SIZE,
          total: users.data?.totalItems ?? 0,
          showSizeChanger: false,
          hideOnSinglePage: true,
          onChange: setPage,
        }}
        columns={[
          {
            title: t('phoneUsers.columns.user'),
            key: 'user',
            render: (_: unknown, user: PhoneUser) => (
              <Flex vertical>
                <Typography.Text strong>
                  {phoneUserLabel(user) ?? t('phoneUsers.noName')}
                </Typography.Text>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {user.username ? `${user.username} · ` : ''}
                  {t('phoneUsers.deviceAccount', { id: shortAccount(user.deviceUserId) })}
                </Typography.Text>
              </Flex>
            ),
          },
          {
            title: t('phoneUsers.columns.lastSeen'),
            dataIndex: 'lastSeenAt',
            render: (value: string, user: PhoneUser) => (
              <span title={t('phoneUsers.firstSeen', { when: when(user.firstSeenAt) })}>{when(value)}</span>
            ),
          },
          {
            title: t('phoneUsers.columns.caver'),
            key: 'caver',
            width: 300,
            render: (_: unknown, user: PhoneUser) =>
              canMap ? (
                <CaverPicker
                  user={user}
                  busy={working === user.deviceUserId}
                  onChoose={(caverId) => void choose(user, caverId)}
                />
              ) : user.caverId ? (
                <span>{user.caverName ?? t('phoneUsers.unnamedCaver')}</span>
              ) : (
                <Typography.Text type="secondary">{t('phoneUsers.notMapped')}</Typography.Text>
              ),
          },
          {
            title: t('phoneUsers.columns.linkedBy'),
            key: 'linkedBy',
            render: (_: unknown, user: PhoneUser) =>
              user.caverId && user.linkSource ? (
                <Tag data-testid={`phone-user-source-${shortAccount(user.deviceUserId)}`}>
                  {t(SOURCE_KEY[user.linkSource])}
                </Tag>
              ) : (
                <Tag color="gold" data-testid={`phone-user-source-${shortAccount(user.deviceUserId)}`}>
                  {t('phoneUsers.notMapped')}
                </Tag>
              ),
          },
          ...(canMap
            ? [
                {
                  title: '',
                  key: 'actions',
                  width: 120,
                  render: (_: unknown, user: PhoneUser) =>
                    user.caverId ? (
                      <Popconfirm
                        title={t('phoneUsers.unlinkConfirm')}
                        description={t('phoneUsers.unlinkConfirmBody')}
                        okText={t('phoneUsers.unlink')}
                        onConfirm={() => void remove(user)}
                      >
                        <Button
                          size="small"
                          disabled={working === user.deviceUserId}
                          data-testid={`phone-user-unlink-${shortAccount(user.deviceUserId)}`}
                        >
                          {t('phoneUsers.unlink')}
                        </Button>
                      </Popconfirm>
                    ) : null,
                },
              ]
            : []),
        ]}
      />
    </div>
  );
}
