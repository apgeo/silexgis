// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { App, DatePicker, Flex, Form, Input, Modal, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  useCreateExpedition,
  useUpdateExpedition,
  type ExpeditionInfo,
  type ExpeditionWrite,
} from '../../api/hooks.ts';
import { tripDateEndForWrite } from '../../components/trips/tripDates.ts';
import TripGeometryField from '../../components/trips/TripGeometryField.tsx';
import type { TripGeometry } from '../../components/trips/tripGeometry.ts';

const { RangePicker } = DatePicker;

interface ExpeditionFormModalProps {
  open: boolean;
  /** The camp being edited, or null to make a new one. */
  camp: ExpeditionInfo | null;
  onClose: (savedId?: string) => void;
}

interface FormValues {
  name: string;
  // Always a range: a one-day camp picks the same day twice, and the equal end is dropped on
  // write — the stored end means "and it ran on to", so one day never reads as a range of itself.
  dates: [Dayjs, Dayjs | null];
  visibility: ExpeditionInfo['visibility'];
  cavingGroupId?: string;
  description?: string;
  geom?: TripGeometry | null;
}

/**
 * The camp editor: what it is called, when it runs, who may read it, which club organises it,
 * what it is about and roughly where it works.
 *
 * Deliberately narrower than the trip's form. A camp carries no roster of its own here — who was
 * there is recorded day by day on the camp's own roster, with the days each person stayed — and
 * no measured facts, because those belong to the trips the camp gathers and the camp only ever
 * adds them up. The lifecycle state is not on this form either: a state moves through the
 * control beside the camp's title, which is the one place the legal moves are offered.
 */
export default function ExpeditionFormModal({ open, camp, onClose }: ExpeditionFormModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const createCamp = useCreateExpedition();
  const updateCamp = useUpdateExpedition();
  const { data: cavingGroups } = useCavingGroups();

  // The map inside the form can only be built once the dialog's open transition has put the
  // content in the document — a map built against a container with no size renders nothing.
  const [shown, setShown] = useState(false);

  useEffect(() => {
    if (open) {
      form.resetFields();
      if (camp) {
        form.setFieldsValue({
          name: camp.name,
          dates: [dayjs(camp.startDate), dayjs(camp.endDate ?? camp.startDate)],
          visibility: camp.visibility,
          cavingGroupId: camp.cavingGroupId ?? undefined,
          description: camp.description ?? undefined,
          geom: camp.geom ?? null,
        });
      } else {
        form.setFieldsValue({
          // Today, both ends: the field is a range, so a bare day would leave it failing its own
          // required rule, and a camp entered without touching the dates is a one-day camp today.
          dates: [dayjs(), dayjs()],
          // A camp is what a club announces, so it starts readable by the club rather than by its
          // author alone; narrowing it is a deliberate choice on this form.
          visibility: 'cavingGroup',
          geom: null,
        });
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reinitialise only when the modal opens
  }, [open]);

  const onOk = async () => {
    let values: FormValues;
    try {
      values = await form.validateFields();
    } catch {
      // The form has drawn the failing fields' own messages; a rejection left to escape here
      // would reach the browser as an unhandled error for an ordinary empty submit.
      return;
    }
    const [start, end] = values.dates;
    const startDate = start.format('YYYY-MM-DD');
    const body: ExpeditionWrite = {
      name: values.name.trim(),
      description: values.description?.trim() || null,
      startDate,
      endDate: tripDateEndForWrite(startDate, end ? end.format('YYYY-MM-DD') : null),
      geom: values.geom ?? null,
      cavingGroupId: values.cavingGroupId ?? null,
      visibility: values.visibility,
    };

    try {
      const saved = camp
        ? await updateCamp.mutateAsync({ id: camp.id, body })
        : await createCamp.mutateAsync(body);
      message.success(t('common.saved'));
      onClose(saved.id);
    } catch {
      message.error(t('common.saveFailed'));
    }
  };

  return (
    <Modal
      title={camp ? t('expeditions.edit') : t('expeditions.new')}
      open={open}
      onCancel={() => onClose()}
      onOk={() => void onOk()}
      confirmLoading={createCamp.isPending || updateCamp.isPending}
      width={720}
      destroyOnHidden
      afterOpenChange={setShown}
    >
      <Form<FormValues> form={form} layout="vertical">
        <Form.Item name="name" label={t('expeditions.nameField')} rules={[{ required: true }]}>
          <Input maxLength={255} data-testid="expedition-form-name" />
        </Form.Item>
        <Flex gap={12}>
          {/* Wider than its neighbour: two dates and a separator do not fit an equal half. */}
          <Form.Item
            name="dates"
            label={t('expeditions.dates')}
            rules={[{ required: true }]}
            style={{ flex: 2 }}
          >
            <RangePicker style={{ width: '100%' }} allowClear={false} />
          </Form.Item>
          <Form.Item
            name="visibility"
            label={t('features.visibility')}
            rules={[{ required: true }]}
            style={{ flex: 1 }}
          >
            <Select
              data-testid="expedition-form-visibility"
              options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map((v) => ({
                value: v,
                label: t(`caves.visibilityValues.${v}`),
              }))}
            />
          </Form.Item>
        </Flex>
        <Form.Item name="cavingGroupId" label={t('expeditions.cavingGroup')}>
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            options={(cavingGroups ?? []).map((group) => ({ value: group.id, label: group.name }))}
          />
        </Form.Item>
        <Form.Item name="description" label={t('features.description')}>
          <Input.TextArea rows={4} maxLength={10000} />
        </Form.Item>
        {/* Roughly where the camp works — the shape somebody draws on the plan, not a position
            anybody navigates by and not derived from the caves its trips reach. It carries no
            location protection of its own, and the warning says so: everybody who may read the
            camp is shown it exactly as drawn. */}
        <Form.Item
          name="geom"
          label={t('expeditions.workingArea')}
          tooltip={t('expeditions.workingAreaHint')}
        >
          <TripGeometryField
            active={shown}
            height={260}
            testId="expedition-working-area"
            warningTitle={t('expeditions.workingAreaWarning')}
            warningDetail={t('expeditions.workingAreaWarningDetail')}
          />
        </Form.Item>
      </Form>
    </Modal>
  );
}
