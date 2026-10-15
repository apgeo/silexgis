// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import {
  DeleteOutlined,
  KeyOutlined,
  LockOutlined,
  MailOutlined,
  UnlockOutlined,
  UserAddOutlined,
  UserOutlined,
} from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Result,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  hasAccessAction,
  useAdminUsers,
  useCapabilities,
  useCreateAdminUser,
  useDeleteAdminUser,
  useMe,
  useReinviteAdminUser,
  useResetAdminUserTwoFactor,
  useSetAdminUserLocked,
  type AdminUser,
  type AdminUserCreated,
} from '../../api/hooks.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';
import UserPermissionGroups from './UserPermissionGroups.tsx';
import { userAdminProblem } from './userAdminProblems.ts';

interface InviteValues {
  email: string;
  displayName: string;
}

/** The members of a validation refusal, keyed by the form field each one belongs to. */
function fieldErrors(error: unknown): { name: keyof InviteValues; errors: string[] }[] {
  if (!(error instanceof ApiError)) {
    return [];
  }

  const errors = error.problem?.errors;
  if (errors === null || typeof errors !== 'object') {
    return [];
  }

  const fields: Record<string, keyof InviteValues> = { Email: 'email', DisplayName: 'displayName' };
  return Object.entries(errors as Record<string, unknown>).flatMap(([member, messages]) => {
    const name = fields[member];
    if (name === undefined) {
      return [];
    }

    const list = Array.isArray(messages) ? messages.map(String) : [String(messages)];
    return [{ name, errors: list }];
  });
}

/**
 * The register of accounts, for whoever administers the installation: who can sign in, and the
 * three things an operator has to be able to do about it — let somebody in, shut an account out,
 * and remove one.
 *
 * <b>Each control follows its own right.</b> Reading the register, inviting, locking and deleting
 * are four separate actions on the accounts domain, and an installation can hand them out apart;
 * a button for an action the caller lacks would only lead to a refusal.
 *
 * <b>The caller's own row has no lock or delete.</b> The server refuses both — there is no way back
 * in from the other side of either — and offering a button the server is certain to refuse is
 * offering a mistake.
 *
 * <b>A second factor is reset, never switched on.</b> A member who has lost both their
 * authenticator and their recovery codes has no way back in alone, so an operator can switch every
 * method off; the confirmation says what that does to the member, because they are the one who
 * will be signed out and mailed. A full administrator's second factor is offered only to another
 * full administrator, which is the rule the server holds to.
 *
 * <b>An opened row shows the account's permission groups</b>, read only when it is opened, and
 * links to where membership is edited rather than editing it here.
 *
 * <b>A hand-over link is shown once.</b> When the installation cannot mail the invitation, the
 * server returns the link instead, and keeps nothing it could show again. So it appears in a
 * dialog that says so and that a stray click outside cannot close; a lost link is replaced by
 * sending a new invitation, never recovered.
 */
export default function UsersPage() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<InviteValues>();

  const [searchInput, setSearchInput] = useState('');
  const search = useDebouncedValue(searchInput).trim();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [inviting, setInviting] = useState(false);
  const [handOver, setHandOver] = useState<AdminUserCreated | null>(null);

  const { data: capabilities } = useCapabilities();
  const rights = capabilities?.domains.users;
  const canCreate = hasAccessAction(rights, 'create');
  const canWrite = hasAccessAction(rights, 'write');
  const canDelete = hasAccessAction(rights, 'delete');
  const canReadPermissionGroups = hasAccessAction(capabilities?.domains.permissionGroups, 'read');
  const callerIsFullAdministrator = capabilities?.isFullAdmin === true;

  const { data: me } = useMe();
  const users = useAdminUsers({ q: search, page, pageSize });
  const create = useCreateAdminUser();
  const reinvite = useReinviteAdminUser();
  const setLocked = useSetAdminUserLocked();
  const remove = useDeleteAdminUser();
  const resetTwoFactor = useResetAdminUserTwoFactor();

  const day = (value: string) => new Date(value).toLocaleDateString(i18n.resolvedLanguage);

  /** What a finished invitation leaves on screen: a note if it was mailed, the link if it was not. */
  const announce = (created: AdminUserCreated) => {
    if (created.setPasswordUrl) {
      setHandOver(created);
    } else {
      message.success(t('admin.users.invitationSent', { email: created.user.email ?? '' }));
    }
  };

  const submitInvite = async (values: InviteValues) => {
    try {
      const created = await create.mutateAsync({
        email: values.email.trim(),
        displayName: values.displayName.trim(),
      });
      setInviting(false);
      form.resetFields();
      announce(created);
    } catch (error) {
      const fields = fieldErrors(error);
      if (error instanceof ApiError && error.code === 'user.email_taken') {
        form.setFields([{ name: 'email', errors: [userAdminProblem(error, t)] }]);
      } else if (fields.length > 0) {
        form.setFields(fields);
      } else {
        message.error(userAdminProblem(error, t));
      }
    }
  };

  const sendAgain = async (user: AdminUser) => {
    try {
      announce(await reinvite.mutateAsync(user.id));
    } catch (error) {
      message.error(userAdminProblem(error, t));
    }
  };

  const toggleLock = async (user: AdminUser) => {
    try {
      await setLocked.mutateAsync({ id: user.id, locked: !user.isLockedOut });
      message.success(
        user.isLockedOut
          ? t('admin.users.unlocked', { name: user.label })
          : t('admin.users.locked', { name: user.label }),
      );
    } catch (error) {
      message.error(userAdminProblem(error, t));
    }
  };

  const deleteUser = async (user: AdminUser) => {
    try {
      await remove.mutateAsync(user.id);
      message.success(t('admin.users.deleted', { name: user.label }));
    } catch (error) {
      message.error(userAdminProblem(error, t));
    }
  };

  const resetSecondFactor = async (user: AdminUser) => {
    try {
      await resetTwoFactor.mutateAsync(user.id);
      message.success(t('admin.users.twoFactorReset', { name: user.label }));
    } catch (error) {
      message.error(userAdminProblem(error, t));
    }
  };

  if (users.error instanceof ApiError && users.error.status === 403) {
    return <Result status="403" title={t('admin.forbidden')} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <UserOutlined /> {t('admin.users.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.users.intro')}
      </Typography.Paragraph>

      <Flex gap={8} wrap align="center">
        <Input.Search
          allowClear
          aria-label={t('admin.users.search')}
          placeholder={t('admin.users.searchPlaceholder')}
          style={{ maxWidth: 320 }}
          value={searchInput}
          onChange={(event) => {
            setSearchInput(event.target.value);
            setPage(1);
          }}
        />
        {canCreate && (
          <Button type="primary" icon={<UserAddOutlined />} onClick={() => setInviting(true)}>
            {t('admin.users.invite')}
          </Button>
        )}
      </Flex>

      <Table<AdminUser>
        rowKey="id"
        size="small"
        scroll={{ x: 'max-content' }}
        loading={users.isFetching && !users.data}
        dataSource={users.data?.items ?? []}
        expandable={{
          expandedRowRender: (user: AdminUser) => (
            <UserPermissionGroups userId={user.id} linkable={canReadPermissionGroups} />
          ),
        }}
        onChange={(pagination) => {
          setPage(pagination.current ?? 1);
          setPageSize(pagination.pageSize ?? 25);
        }}
        pagination={{
          current: users.data?.page ?? page,
          pageSize: users.data?.pageSize ?? pageSize,
          total: users.data?.totalItems ?? 0,
          showSizeChanger: true,
        }}
        columns={[
          {
            title: t('admin.users.name'),
            key: 'name',
            render: (_: unknown, user: AdminUser) => (
              <Space size={4} wrap>
                <Typography.Text>{user.label}</Typography.Text>
                {user.id === me?.id && <Tag>{t('admin.users.you')}</Tag>}
              </Space>
            ),
          },
          { title: t('admin.users.email'), dataIndex: 'email' },
          {
            title: t('admin.users.status'),
            key: 'status',
            render: (_: unknown, user: AdminUser) => (
              <Space size={4} wrap>
                {user.isLockedOut ? (
                  <Tag color="red">{t('admin.users.statusLocked')}</Tag>
                ) : (
                  <Tag color="green">{t('admin.users.statusActive')}</Tag>
                )}
                {!user.hasPassword && <Tag color="gold">{t('admin.users.statusNoPassword')}</Tag>}
                {user.isFullAdministrator && <Tag color="blue">{t('admin.users.statusAdministrator')}</Tag>}
                {user.twoFactorEnabled && <Tag color="cyan">{t('admin.users.statusTwoFactor')}</Tag>}
              </Space>
            ),
          },
          {
            title: t('admin.users.createdAt'),
            dataIndex: 'createdAt',
            render: (value: string) => day(value),
          },
          {
            title: t('admin.users.actions'),
            key: 'actions',
            render: (_: unknown, user: AdminUser) => {
              const self = user.id === me?.id;
              // A right over accounts below full administration stops at a full administrator's
              // account; the server refuses it as well (a locked one is not flagged here, so for
              // that case the refusal is what the operator sees).
              const withinReach = !user.isFullAdministrator || callerIsFullAdministrator;
              return (
                <Space size={4} wrap>
                  {canWrite && !self && withinReach && (
                    <Button
                      size="small"
                      icon={user.isLockedOut ? <UnlockOutlined /> : <LockOutlined />}
                      loading={setLocked.isPending && setLocked.variables?.id === user.id}
                      onClick={() => void toggleLock(user)}
                    >
                      {user.isLockedOut ? t('admin.users.unlock') : t('admin.users.lock')}
                    </Button>
                  )}
                  {canWrite && !user.hasPassword && (
                    <Button
                      size="small"
                      icon={<MailOutlined />}
                      loading={reinvite.isPending && reinvite.variables === user.id}
                      onClick={() => void sendAgain(user)}
                    >
                      {t('admin.users.sendAgain')}
                    </Button>
                  )}
                  {canWrite &&
                    !self &&
                    user.twoFactorEnabled &&
                    withinReach && (
                      <Popconfirm
                        title={t('admin.users.resetTwoFactorConfirm', { name: user.label })}
                        description={
                          <Typography.Paragraph style={{ maxWidth: 360, margin: 0 }}>
                            {t('admin.users.resetTwoFactorConfirmDetail')}
                          </Typography.Paragraph>
                        }
                        okText={t('admin.users.resetTwoFactor')}
                        okButtonProps={{ danger: true }}
                        onConfirm={() => resetSecondFactor(user)}
                      >
                        <Button
                          size="small"
                          icon={<KeyOutlined />}
                          loading={resetTwoFactor.isPending && resetTwoFactor.variables === user.id}
                        >
                          {t('admin.users.resetTwoFactor')}
                        </Button>
                      </Popconfirm>
                    )}
                  {canDelete && !self && withinReach && (
                    <Popconfirm
                      title={t('admin.users.deleteConfirm', { name: user.label })}
                      description={t('admin.users.deleteConfirmDetail')}
                      okText={t('admin.users.delete')}
                      okButtonProps={{ danger: true }}
                      onConfirm={() => deleteUser(user)}
                    >
                      <Button size="small" danger icon={<DeleteOutlined />}>
                        {t('admin.users.delete')}
                      </Button>
                    </Popconfirm>
                  )}
                </Space>
              );
            },
          },
        ]}
      />

      <Modal
        open={inviting}
        title={t('admin.users.inviteTitle')}
        okText={t('admin.users.inviteSubmit')}
        confirmLoading={create.isPending}
        onOk={() => form.submit()}
        onCancel={() => {
          setInviting(false);
          form.resetFields();
        }}
        destroyOnHidden
      >
        <Typography.Paragraph type="secondary">{t('admin.users.inviteIntro')}</Typography.Paragraph>
        <Form<InviteValues> form={form} layout="vertical" onFinish={(values) => void submitInvite(values)}>
          <Form.Item
            name="displayName"
            label={t('admin.users.inviteName')}
            rules={[{ required: true, whitespace: true, message: t('admin.users.inviteNameRequired') }]}
          >
            <Input maxLength={100} autoComplete="off" />
          </Form.Item>
          <Form.Item
            name="email"
            label={t('admin.users.inviteEmail')}
            rules={[
              { required: true, message: t('admin.users.inviteEmailRequired') },
              { type: 'email', message: t('admin.users.inviteEmailInvalid') },
            ]}
          >
            <Input maxLength={256} autoComplete="off" />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={handOver !== null}
        title={t('admin.users.handOverTitle')}
        mask={{ closable: false }}
        keyboard={false}
        onCancel={() => setHandOver(null)}
        footer={
          <Button type="primary" onClick={() => setHandOver(null)}>
            {t('admin.users.handOverDone')}
          </Button>
        }
      >
        {handOver && (
          <Flex vertical gap={12}>
            <Typography.Paragraph style={{ margin: 0 }}>
              {t('admin.users.handOverIntro', { name: handOver.user.label, email: handOver.user.email ?? '' })}
            </Typography.Paragraph>
            <Typography.Paragraph
              code
              copyable={{ text: handOver.setPasswordUrl ?? '' }}
              style={{ margin: 0, wordBreak: 'break-all' }}
              data-testid="hand-over-link"
            >
              {handOver.setPasswordUrl}
            </Typography.Paragraph>
            <Alert
              type="warning"
              showIcon
              title={t('admin.users.handOverOnce', { hours: handOver.expiresHours })}
            />
          </Flex>
        )}
      </Modal>
    </Flex>
  );
}
