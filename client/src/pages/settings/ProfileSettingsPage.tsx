// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { App, Alert, Button, Card, Flex, Form, Input, Select, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import {
  useCavingGroups,
  useMe,
  useUpdateProfile,
  type FieldVisibility,
  type MeUpdate,
} from '../../api/hooks.ts';
import AddressList from '../../components/settings/AddressList.tsx';
import AvatarPicker from '../../components/settings/AvatarPicker.tsx';
import FieldVisibilityToggle from '../../components/settings/FieldVisibilityToggle.tsx';

interface FormValues {
  firstName?: string;
  lastName?: string;
  displayName?: string;
  bio?: string;
  cavingClubId?: string;
  visibility: Record<keyof MeUpdate['visibility'], FieldVisibility>;
}

/**
 * The account holder's profile. Everything on the form is saved by one button, including the
 * per-field audience choices — privacy is not a separate save.
 *
 * The address list sits below with its own per-row saves, because each address is its own
 * resource; the email address and the user name are shown read-only with a link to the section
 * that owns them, so there is exactly one place each can be changed.
 */
export default function ProfileSettingsPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const { data: me, isPending, isError, refetch } = useMe();
  const { data: cavingGroups } = useCavingGroups();
  const update = useUpdateProfile();

  // Filled once per account, not on every refetch. Adding an address invalidates the profile
  // query, and re-running this on the response would overwrite whatever the user had typed into
  // the name fields but not yet saved.
  const filledFor = useRef<string | null>(null);

  useEffect(() => {
    if (me && filledFor.current !== me.id) {
      filledFor.current = me.id;
      form.setFieldsValue({
        firstName: me.firstName ?? undefined,
        lastName: me.lastName ?? undefined,
        displayName: me.displayName ?? undefined,
        bio: me.bio ?? undefined,
        cavingClubId: me.cavingClubId ?? undefined,
        visibility: me.visibility,
      });
    }
  }, [me, form]);

  if (isError) {
    return (
      <Alert
        type="error"
        showIcon
        title={t('common.loadFailed')}
        action={<Button onClick={() => void refetch()}>{t('common.retry')}</Button>}
      />
    );
  }

  if (isPending || !me) {
    return <Spin />;
  }

  const onFinish = async (values: FormValues) => {
    try {
      await update.mutateAsync({
        firstName: values.firstName ?? null,
        lastName: values.lastName ?? null,
        displayName: values.displayName ?? null,
        bio: values.bio ?? null,
        cavingClubId: values.cavingClubId ?? null,
        visibility: values.visibility,
      });
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  /** A field label with its audience control beside it. */
  const labelWith = (label: string, field: keyof FormValues['visibility']) => (
    <Flex gap={4} align="center">
      {label}
      <Form.Item name={['visibility', field]} noStyle>
        <FieldVisibilityToggle />
      </Form.Item>
    </Flex>
  );

  return (
    <Form form={form} layout="vertical" onFinish={(v) => void onFinish(v)}>
      <Flex vertical gap={16}>
        <Typography.Text type="secondary">{t('settings.visibility.legend')}</Typography.Text>

        <Card size="small" title={t('settings.profile.identity')}>
          <Flex vertical gap={0}>
            <Form.Item label={t('settings.profile.username')}>
              <Flex gap={8} align="center" wrap>
                <Typography.Text strong>{me.userName}</Typography.Text>
                <Link to="/settings/account">{t('settings.profile.manageElsewhere')}</Link>
              </Flex>
            </Form.Item>
            <Form.Item label={t('settings.profile.email')}>
              <Flex gap={8} align="center" wrap>
                <Typography.Text strong>{me.email}</Typography.Text>
                <Link to="/settings/emails">{t('settings.profile.manageElsewhere')}</Link>
                <Form.Item name={['visibility', 'email']} noStyle>
                  <FieldVisibilityToggle />
                </Form.Item>
              </Flex>
            </Form.Item>
            <Flex gap={12} wrap>
              <Form.Item
                name="firstName"
                label={labelWith(t('settings.profile.firstName'), 'realName')}
                style={{ flex: 1, minWidth: 200 }}
              >
                <Input maxLength={100} />
              </Form.Item>
              <Form.Item name="lastName" label={t('settings.profile.lastName')} style={{ flex: 1, minWidth: 200 }}>
                <Input maxLength={100} />
              </Form.Item>
            </Flex>
            <Form.Item
              name="displayName"
              label={t('settings.profile.displayName')}
              extra={t('settings.profile.displayNameHint')}
            >
              <Input maxLength={100} />
            </Form.Item>
          </Flex>
        </Card>

        <Card size="small" title={t('settings.profile.about')}>
          <Form.Item name="bio" label={labelWith(t('settings.profile.bio'), 'bio')}>
            <Input.TextArea rows={4} maxLength={2000} showCount />
          </Form.Item>
          <Flex gap={12} wrap>
            {/* Shown, never edited here: there is one phone column and it is the sign-in
                number, which only takes a new value once a code texted to it comes back. The
                audience choice beside the label is still a profile field, so it stays. */}
            <Form.Item
              label={labelWith(t('settings.profile.phone'), 'phone')}
              style={{ flex: 1, minWidth: 200 }}
              help={
                <Link to="/settings/security">{t('settings.profile.phoneManagedInSecurity')}</Link>
              }
            >
              <Input
                readOnly
                value={me.phoneNumber ?? ''}
                placeholder={t('settings.profile.phoneNone')}
              />
            </Form.Item>
            <Form.Item
              name="cavingClubId"
              label={labelWith(t('settings.profile.cavingClub'), 'cavingClub')}
              style={{ flex: 1, minWidth: 200 }}
            >
              {/* The club you say you are with, which is your own statement — separate from the
                  rosters clubs keep, and governed by the visibility setting beside the label. */}
              <Select
                allowClear
                showSearch
                optionFilterProp="label"
                options={(cavingGroups ?? []).map((group) => ({ value: group.id, label: group.name }))}
              />
            </Form.Item>
          </Flex>
        </Card>

        <Card size="small" title={t('settings.profile.avatar')}>
          <AvatarPicker me={me} />
        </Card>

        <Card
          size="small"
          title={
            <Flex gap={8} align="center" wrap>
              {t('settings.profile.addresses')}
              <Form.Item name={['visibility', 'address']} noStyle>
                <FieldVisibilityToggle />
              </Form.Item>
              <Typography.Text type="secondary">{t('settings.profile.locationAudience')}</Typography.Text>
              <Form.Item name={['visibility', 'addressPoint']} noStyle>
                <FieldVisibilityToggle />
              </Form.Item>
            </Flex>
          }
        >
          <Flex vertical gap={12}>
            <Typography.Text type="secondary">{t('settings.profile.addressesHint')}</Typography.Text>
            <AddressList addresses={me.addresses} />
          </Flex>
        </Card>

        <Flex justify="end">
          <Button type="primary" htmlType="submit" loading={update.isPending}>
            {t('common.save')}
          </Button>
        </Flex>
      </Flex>
    </Form>
  );
}
