// SPDX-License-Identifier: AGPL-3.0-or-later
import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { App, Button, Card, Empty, Flex, Form, Input, Modal, Popconfirm, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCreateAddress,
  useDeleteAddress,
  useUpdateAddress,
  type UserAddress,
  type UserAddressWrite,
} from '../../api/hooks.ts';
import { formatLonLat } from '../../geo/coords.ts';
import PointField from './PointField.tsx';

/** Mirrors the server's cap; the list is a few places a person lives, not storage. */
const MAX_ADDRESSES = 10;

interface FormValues {
  label: string;
  country?: string;
  city?: string;
  addressText?: string;
  point: [number, number] | null;
}

interface Props {
  addresses: UserAddress[];
}

/**
 * The account's addresses. Each has its own endpoint, so each row saves on its own rather than
 * riding on the profile form — which also means adding one cannot lose an unsaved profile edit.
 */
export default function AddressList({ addresses }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const [editing, setEditing] = useState<UserAddress | null>(null);
  const [open, setOpen] = useState(false);

  const create = useCreateAddress();
  const update = useUpdateAddress();
  const remove = useDeleteAddress();

  const openEditor = (address: UserAddress | null) => {
    setEditing(address);
    setOpen(true);
    form.setFieldsValue({
      label: address?.label ?? '',
      country: address?.country ?? undefined,
      city: address?.city ?? undefined,
      addressText: address?.addressText ?? undefined,
      point: address?.geom ? [address.geom.coordinates[0], address.geom.coordinates[1]] : null,
    });
  };

  const submit = async () => {
    const values = await form.validateFields();
    const body: UserAddressWrite = {
      label: values.label,
      country: values.country ?? null,
      city: values.city ?? null,
      addressText: values.addressText ?? null,
      geom: values.point ? { type: 'Point', coordinates: values.point } : null,
      sortOrder: editing?.sortOrder ?? addresses.length,
    };

    try {
      if (editing) {
        await update.mutateAsync({ id: editing.id, body });
      } else {
        await create.mutateAsync(body);
      }
      setOpen(false);
      message.success(t('common.saved'));
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Flex vertical gap={12}>
      {addresses.length === 0 && (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('settings.profile.noAddresses')} />
      )}

      {addresses.map((address) => (
        <Card
          key={address.id}
          size="small"
          title={address.label}
          extra={
            <Flex gap={4}>
              <Button
                type="text"
                size="small"
                icon={<EditOutlined />}
                aria-label={t('settings.profile.editAddress')}
                onClick={() => openEditor(address)}
              />
              <Popconfirm
                title={t('settings.profile.removeAddressConfirm')}
                onConfirm={() => {
                  remove.mutate(address.id, {
                    onSuccess: () => message.success(t('common.deleted')),
                    onError: () => message.error(t('common.saveFailed')),
                  });
                }}
              >
                <Button
                  type="text"
                  size="small"
                  danger
                  icon={<DeleteOutlined />}
                  aria-label={t('settings.profile.removeAddress')}
                />
              </Popconfirm>
            </Flex>
          }
        >
          <Flex vertical gap={2}>
            <Typography.Text>
              {[address.city, address.country].filter(Boolean).join(', ') || '—'}
            </Typography.Text>
            {address.addressText && (
              <Typography.Text type="secondary">{address.addressText}</Typography.Text>
            )}
            {address.geom && (
              <Typography.Text type="secondary">
                {formatLonLat(address.geom.coordinates[0], address.geom.coordinates[1])}
              </Typography.Text>
            )}
          </Flex>
        </Card>
      ))}

      <Button
        type="dashed"
        block
        icon={<PlusOutlined />}
        disabled={addresses.length >= MAX_ADDRESSES}
        onClick={() => openEditor(null)}
      >
        {t('settings.profile.addAddress')}
      </Button>

      <Modal
        open={open}
        destroyOnHidden
        title={editing ? t('settings.profile.editAddress') : t('settings.profile.addAddress')}
        okText={t('common.save')}
        confirmLoading={create.isPending || update.isPending}
        onOk={() => void submit()}
        onCancel={() => setOpen(false)}
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="label"
            label={t('settings.profile.addressLabel')}
            rules={[{ required: true }, { max: 100 }]}
          >
            <Input />
          </Form.Item>
          <Flex gap={12} wrap>
            <Form.Item name="country" label={t('settings.profile.country')} style={{ flex: 1, minWidth: 180 }}>
              <Input maxLength={100} />
            </Form.Item>
            <Form.Item name="city" label={t('settings.profile.city')} style={{ flex: 1, minWidth: 180 }}>
              <Input maxLength={100} />
            </Form.Item>
          </Flex>
          <Form.Item name="addressText" label={t('settings.profile.addressText')}>
            <Input.TextArea rows={2} maxLength={4000} />
          </Form.Item>
          <Form.Item name="point" label={t('settings.profile.location')}>
            <PointField />
          </Form.Item>
        </Form>
      </Modal>
    </Flex>
  );
}
