// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { DownloadOutlined, FileTextOutlined, PlusOutlined, UploadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Switch,
  Table,
  Tag,
  Typography,
  Upload,
} from 'antd';
import type { UploadFile } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { downloadFile, tripReportTemplateDefaultUrl } from '../../api/download.ts';
import {
  hasAccessAction,
  useCapabilities,
  useCreateTripReportTemplate,
  useDeleteTripReportTemplate,
  useTripReportTemplates,
  useUpdateTripReportTemplate,
  type TripReportTemplate,
  type TripReportTemplateWrite,
} from '../../api/hooks.ts';

/**
 * Which kind of thing a layout writes up. Named on every save rather than defaulted: the kind
 * decides which vocabulary the body is read under and which write-ups may use it, and a save that
 * left it out would have one chosen for it — quietly retyping a camp layout as a trip layout and
 * taking the club's chosen trip layout with it.
 */
type ReportTemplateKind = NonNullable<TripReportTemplateWrite['kind']>;

const emptyDraft: TripReportTemplateWrite = { name: '', body: '', isDefault: false, kind: 'trip' };

/**
 * The layouts a trip or a camp is written up in.
 *
 * The work happens in a text editor, not here: somebody downloads the layout the system ships,
 * edits it where they can see the whole thing at once, and brings it back. So this page is a way
 * in and a way out — the shipped layout as a file, the club's own layouts as rows — rather than an
 * editor pretending to be one. The box below is there for a two-word change nobody would open an
 * editor for.
 *
 * The shipped file documents its own vocabulary in its own comments, which is why nothing here
 * lists the substitutions: a list on this page and a list in the file are two lists, and the one
 * in the file is the one the person editing is looking at.
 *
 * A layout that cannot be read is refused when it is stored rather than when a document is
 * produced, and the refusal names the line — so it is shown in the server's own words, which are
 * the only words that can name a line.
 */
export default function TripReportTemplatesPage() {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.taxonomies, 'read');
  const canWrite = hasAccessAction(capabilities?.domains.taxonomies, 'write');
  const { data: templates, isLoading } = useTripReportTemplates();
  const createTemplate = useCreateTripReportTemplate();
  const updateTemplate = useUpdateTripReportTemplate();
  const deleteTemplate = useDeleteTripReportTemplate();
  const [editing, setEditing] = useState<TripReportTemplate | null>(null);
  const [creating, setCreating] = useState(false);
  const [shippedKind, setShippedKind] = useState<ReportTemplateKind>('trip');
  const [form] = Form.useForm<TripReportTemplateWrite>();

  const kindOptions: { value: ReportTemplateKind; label: string }[] = [
    { value: 'trip', label: t('admin.reportTemplates.kindTrip') },
    { value: 'expedition', label: t('admin.reportTemplates.kindExpedition') },
  ];
  const kindLabel = (kind: ReportTemplateKind) =>
    kindOptions.find((option) => option.value === kind)?.label ?? kind;

  const open = editing !== null || creating;
  useEffect(() => {
    if (open) {
      form.setFieldsValue(
        editing
          ? {
              name: editing.name,
              body: editing.body,
              isDefault: editing.isDefault,
              kind: editing.kind,
            }
          : emptyDraft,
      );
    }
  }, [open, editing, form]);

  const close = () => {
    setEditing(null);
    setCreating(false);
  };

  const save = async () => {
    const values = await form.validateFields();
    const body: TripReportTemplateWrite = {
      name: values.name.trim(),
      body: values.body,
      isDefault: values.isDefault,
      kind: values.kind,
    };

    try {
      if (editing) {
        await updateTemplate.mutateAsync({ ...body, id: editing.id });
      } else {
        await createTemplate.mutateAsync(body);
      }
      message.success(t('common.saved'));
      close();
    } catch (error) {
      message.error(problemMessage(error, t));
    }
  };

  const remove = async (template: TripReportTemplate) => {
    try {
      await deleteTemplate.mutateAsync(template.id);
      message.success(t('common.deleted'));
    } catch (error) {
      message.error(problemMessage(error, t));
    }
  };

  /** Reads a chosen file into the body box; the browser never uploads it anywhere. */
  const load = async (file: UploadFile) => {
    const blob = file.originFileObj ?? (file as unknown as Blob);
    const text = await new Blob([blob]).text();
    // A file edited on Windows comes back with carriage returns and a byte-order mark, neither of
    // which is part of a line the parser is asked to read.
    form.setFieldValue('body', text.replace(/^﻿/, '').replace(/\r\n/g, '\n'));
  };

  if (!capabilities) {
    return null;
  }
  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <FileTextOutlined /> {t('admin.reportTemplates.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('admin.reportTemplates.intro')}
      </Typography.Paragraph>

      <Flex gap={8} wrap align="center">
        {/* Which shipped layout to start from. The two vocabularies are different, and the file
            is the only place either of them is written down. */}
        <Select
          value={shippedKind}
          onChange={setShippedKind}
          options={kindOptions}
          style={{ minWidth: 180 }}
          aria-label={t('admin.reportTemplates.kind')}
          data-testid="report-template-shipped-kind"
        />
        <Button
          icon={<DownloadOutlined />}
          data-testid="report-template-shipped"
          onClick={() => {
            downloadFile(tripReportTemplateDefaultUrl(shippedKind)).catch(() =>
              message.error(t('admin.reportTemplates.downloadFailed')),
            );
          }}
        >
          {t('admin.reportTemplates.downloadShipped')}
        </Button>
        {canWrite && (
          <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>
            {t('admin.reportTemplates.add')}
          </Button>
        )}
      </Flex>

      <Table
        rowKey="id"
        loading={isLoading}
        dataSource={templates ?? []}
        pagination={false}
        size="small"
        scroll={{ x: true }}
        locale={{ emptyText: t('admin.reportTemplates.none') }}
        columns={[
          { title: t('admin.reportTemplates.name'), dataIndex: 'name' },
          {
            // Both kinds are listed together, so the column is what tells them apart — and each
            // kind has a chosen layout of its own, which would otherwise read as two rows
            // contradicting each other about which one is used.
            title: t('admin.reportTemplates.kind'),
            key: 'kind',
            render: (_: unknown, template: TripReportTemplate) => kindLabel(template.kind),
          },
          {
            title: t('admin.reportTemplates.used'),
            key: 'isDefault',
            render: (_: unknown, template: TripReportTemplate) =>
              template.isDefault ? <Tag color="blue">{t('admin.reportTemplates.isDefault')}</Tag> : '',
          },
          ...(canWrite
            ? [
                {
                  title: '',
                  key: 'actions',
                  render: (_: unknown, template: TripReportTemplate) => (
                    <Flex gap={8}>
                      <Button size="small" onClick={() => setEditing(template)}>
                        {t('admin.reportTemplates.editAction')}
                      </Button>
                      <Popconfirm
                        title={t('admin.reportTemplates.deleteConfirm')}
                        onConfirm={() => void remove(template)}
                      >
                        <Button size="small" danger>
                          {t('admin.reportTemplates.delete')}
                        </Button>
                      </Popconfirm>
                    </Flex>
                  ),
                },
              ]
            : []),
        ]}
      />

      <Modal
        open={open}
        title={editing ? t('admin.reportTemplates.edit') : t('admin.reportTemplates.add')}
        onCancel={close}
        onOk={() => void save()}
        confirmLoading={createTemplate.isPending || updateTemplate.isPending}
        okText={t('common.save')}
        cancelText={t('common.cancel')}
        destroyOnHidden
        width={720}
      >
        <Form form={form} layout="vertical" initialValues={emptyDraft}>
          <Form.Item name="name" label={t('admin.reportTemplates.name')} rules={[{ required: true, max: 120 }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="kind"
            label={t('admin.reportTemplates.kind')}
            extra={t('admin.reportTemplates.kindHint')}
            rules={[{ required: true }]}
          >
            <Select options={kindOptions} data-testid="report-template-kind" />
          </Form.Item>
          <Form.Item label={t('admin.reportTemplates.fromFile')} extra={t('admin.reportTemplates.fromFileHint')}>
            <Upload
              accept=".txt"
              maxCount={1}
              showUploadList={false}
              // Nothing is transferred: the file is read here and its text becomes the box below,
              // so an edited layout is reviewed before it is stored rather than after.
              beforeUpload={(file) => {
                void load(file as unknown as UploadFile);
                return false;
              }}
            >
              <Button icon={<UploadOutlined />} data-testid="report-template-load">
                {t('admin.reportTemplates.chooseFile')}
              </Button>
            </Upload>
          </Form.Item>
          <Form.Item name="body" label={t('admin.reportTemplates.body')} rules={[{ required: true }]}>
            <Input.TextArea
              rows={16}
              data-testid="report-template-body"
              style={{ fontFamily: 'monospace', whiteSpace: 'pre' }}
            />
          </Form.Item>
          <Form.Item
            name="isDefault"
            label={t('admin.reportTemplates.isDefault')}
            extra={t('admin.reportTemplates.isDefaultHint')}
            valuePropName="checked"
          >
            <Switch />
          </Form.Item>
        </Form>
      </Modal>
    </Flex>
  );
}

/**
 * A layout the parser refused says which line is wrong, and that sentence is the server's — no
 * wording held here could name a line of a file this page never parsed.
 */
function problemMessage(error: unknown, t: ReturnType<typeof useTranslation>['t']): string {
  if (!(error instanceof ApiError)) {
    return t('common.saveFailed');
  }

  if (error.code === 'trip_report_template.invalid') {
    return error.detail ?? t('admin.reportTemplates.invalid');
  }
  return t('common.saveFailed');
}
