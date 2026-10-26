// SPDX-License-Identifier: AGPL-3.0-or-later
import { KeyOutlined } from '@ant-design/icons';
import { Alert, App, Card, Form, Input, Switch, Typography } from 'antd';
import Button from '../../components/Button.tsx';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { useChangePassword } from '../../api/hooks.ts';
import { signOutAfterPasswordChange } from '../../auth/signOutAfterPasswordChange.ts';

interface PasswordValues {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
  revokeAccessKeys?: boolean;
}

/**
 * The shortest password the server accepts. The server's rule is length only — no character
 * classes — so this is the whole of what can be checked before asking it.
 */
const MIN_LENGTH = 10;

/**
 * Changing the password of an account that has one.
 *
 * Success is not the end of the form but of the session: the server ends every renewable session
 * of the account, this browser's included, so the reader is signed out here and sent to sign in
 * with the password they just chose. They are told so before they press the button, because a
 * form that logs you out on success is otherwise indistinguishable from one that failed.
 *
 * The account's personal access keys — the ones a field device uploads with — are a different
 * matter: they keep working through a password change unless the reader switches them off here
 * too, because a device in a cave cannot be given a new key from there. The switch starts off.
 *
 * The server's refusals land on the field they are about: a wrong current password on the first
 * box, a new password it will not take on the second.
 */
export default function PasswordChangeCard({
  onChanged = signOutAfterPasswordChange,
}: {
  /** What happens once the server has accepted the change. Replaced only by tests. */
  onChanged?: () => Promise<void> | void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<PasswordValues>();
  const change = useChangePassword();

  const onFinish = async (values: PasswordValues) => {
    try {
      await change.mutateAsync({
        currentPassword: values.currentPassword,
        newPassword: values.newPassword,
        revokeAccessKeys: values.revokeAccessKeys ?? false,
      });
    } catch (error) {
      if (!(error instanceof ApiError)) {
        message.error(t('common.saveFailed'));
        return;
      }

      if (error.code === 'me.password_incorrect') {
        form.setFields([{ name: 'currentPassword', errors: [t('security.password.incorrect')] }]);
      } else if (error.code === 'me.password_invalid') {
        form.setFields([{ name: 'newPassword', errors: [t('security.password.rejected')] }]);
      } else if (error.code === 'validation.failed') {
        const errors = (error.problem?.errors ?? {}) as Record<string, unknown>;
        form.setFields([
          ...(errors.CurrentPassword ? [{ name: 'currentPassword' as const, errors: [t('security.password.currentRequired')] }] : []),
          ...(errors.NewPassword ? [{ name: 'newPassword' as const, errors: [t('auth.passwordTooShort')] }] : []),
        ]);
      } else if (error.code === 'me.password_not_set') {
        message.error(t('security.password.notSet'));
      } else {
        message.error(t('common.saveFailed'));
      }
      return;
    }

    form.resetFields();
    message.success(t('security.password.changed'));
    await onChanged();
  };

  return (
    <Card
      size="small"
      title={
        <span>
          <KeyOutlined /> {t('security.password.title')}
        </span>
      }
    >
      <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
        {t('security.password.intro')}
      </Typography.Paragraph>
      <Form<PasswordValues>
        form={form}
        layout="vertical"
        requiredMark={false}
        onFinish={(values) => void onFinish(values)}
        style={{ maxWidth: 400 }}
      >
        <Form.Item
          name="currentPassword"
          label={t('security.password.current')}
          rules={[{ required: true, message: t('security.password.currentRequired') }]}
        >
          <Input.Password autoComplete="current-password" />
        </Form.Item>
        <Form.Item
          name="newPassword"
          label={t('auth.newPassword')}
          rules={[
            { required: true, message: t('auth.passwordTooShort') },
            { min: MIN_LENGTH, message: t('auth.passwordTooShort') },
          ]}
        >
          <Input.Password autoComplete="new-password" />
        </Form.Item>
        <Form.Item
          name="confirmPassword"
          label={t('auth.confirmPassword')}
          dependencies={['newPassword']}
          rules={[
            { required: true, message: t('auth.passwordsDiffer') },
            ({ getFieldValue }) => ({
              validator: (_, value) =>
                !value || getFieldValue('newPassword') === value
                  ? Promise.resolve()
                  : Promise.reject(new Error(t('auth.passwordsDiffer'))),
            }),
          ]}
        >
          <Input.Password autoComplete="new-password" />
        </Form.Item>
        <Form.Item
          name="revokeAccessKeys"
          label={t('security.password.revokeKeys')}
          valuePropName="checked"
          initialValue={false}
          extra={t('security.password.revokeKeysHint')}
        >
          <Switch data-testid="password-revoke-access-keys" />
        </Form.Item>
        <Alert type="info" showIcon title={t('security.password.signsOut')} style={{ marginBottom: 16 }} />
        <Button type="primary" htmlType="submit" loading={change.isPending}>
          {t('security.password.submit')}
        </Button>
      </Form>
    </Card>
  );
}
