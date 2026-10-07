// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { App, DatePicker, Form, Input, Modal, Select, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import {
  useCreateExpeditionRosterEntry,
  useExpeditionRosterRoles,
  useUpdateExpeditionRosterEntry,
  type ExpeditionRosterEntry,
  type ExpeditionRosterEntryWrite,
} from '../../api/hooks.ts';
import CaverNameField from '../../components/cavers/CaverNameField.tsx';
import { expeditionRosterRoleLabel } from '../../components/expeditions/rosterRoles.ts';
import { caverReference, type CaverReferenceRow } from '../../components/trips/roster.ts';
import { tripDateEndForWrite } from '../../components/trips/tripDates.ts';

const { RangePicker } = DatePicker;

/**
 * The one refusal this dialog has words of its own for: the entry the row was holding is not in
 * the directory any more — merged into another or removed since the roster was read.
 */
const CAVER_UNKNOWN = 'expedition_roster.caver_unknown';

/**
 * The role a stay starts out as. The one shipped code the server itself leans on: most people at
 * a camp were simply there, and a dialog that opened on an empty role would make the ordinary
 * case a choice.
 */
const DEFAULT_ROLE_CODE = 'member';

/** The longest note the server will store against a stay; a longer one is refused. */
const NOTE_MAX = 500;

interface ExpeditionStayModalProps {
  open: boolean;
  expeditionId: string;
  /** The camp's first day, and its last where it ran on: the days a new stay starts out as. */
  campStart: string;
  campEnd?: string | null;
  /** The stay being corrected, or null to record a new one. */
  entry: ExpeditionRosterEntry | null;
  onClose: () => void;
}

interface FormValues {
  person: CaverReferenceRow;
  roleId: number;
  // Always a range, on the camp's own convention: somebody there for one day picks that day
  // twice, and the equal end is dropped on write — a stored end means "and they stayed on to",
  // so one day never reads as a range of itself.
  dates: [Dayjs, Dayjs | null];
  note?: string;
}

/**
 * Records that somebody was at a camp for a stretch of days, or corrects a stay already recorded.
 *
 * A stay is one person in one role. It is not the person: somebody who cooked and also surveyed
 * is two stays, and somebody who left and came back is two stays, which is why nothing here
 * looks for a stay the person already has and offers to change that one instead. The dialog says
 * so, because the opposite assumption — one row per person — is the natural one.
 *
 * Who the person is, is said with one text box: somebody out of the directory, or a name it does
 * not hold yet. Which of the two a row turns out to be is read by the rule every form that names
 * a person shares, and what a typed name then means is decided on the server — see the field.
 */
export default function ExpeditionStayModal({
  open,
  expeditionId,
  campStart,
  campEnd,
  entry,
  onClose,
}: ExpeditionStayModalProps) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<FormValues>();
  const { data: roles } = useExpeditionRosterRoles();
  const createStay = useCreateExpeditionRosterEntry(expeditionId);
  const updateStay = useUpdateExpeditionRosterEntry(expeditionId);
  const saving = createStay.isPending || updateStay.isPending;

  useEffect(() => {
    if (open) {
      form.resetFields();
      if (entry) {
        form.setFieldsValue({
          // Loaded under the name the roster showed, which for somebody with an account is their
          // own label and need not be the name the directory records. Left alone, the row still
          // means that person; typed over, it means whoever the new text names.
          person: { caverId: entry.caverId, loadedName: entry.caverName, name: entry.caverName },
          roleId: entry.roleId,
          dates: [dayjs(entry.fromDate), dayjs(entry.toDate ?? entry.fromDate)],
          note: entry.note ?? undefined,
        });
      } else {
        form.setFieldsValue({
          person: { name: '' },
          // The whole camp: most people were there for all of it, and whoever was not corrects
          // two dates rather than typing two.
          dates: [dayjs(campStart), dayjs(campEnd ?? campStart)],
        });
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reinitialise only when the modal opens
  }, [open]);

  // The ordinary role, once the vocabulary is in — usually it already is, because the list
  // behind this dialog draws from it. Written only into a new stay nobody has given a role yet,
  // so an answer arriving late cannot take back a choice.
  const defaultRoleId = roles?.find((role) => role.code === DEFAULT_ROLE_CODE)?.id;
  useEffect(() => {
    const unset = () => form.getFieldValue('roleId') === undefined;
    if (open && !entry && defaultRoleId !== undefined && unset()) {
      form.setFieldsValue({ roleId: defaultRoleId });
    }
  }, [open, entry, defaultRoleId, form]);

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
    const fromDate = start.format('YYYY-MM-DD');
    const body: ExpeditionRosterEntryWrite = {
      // An entry or a name, exactly one of them, by the one rule that decides it.
      ...caverReference(values.person),
      roleId: values.roleId,
      fromDate,
      toDate: tripDateEndForWrite(fromDate, end ? end.format('YYYY-MM-DD') : null),
      note: values.note?.trim() || null,
    };

    try {
      if (entry) {
        await updateStay.mutateAsync({ entryId: entry.id, body });
      } else {
        await createStay.mutateAsync(body);
      }
      message.success(t('common.saved'));
      onClose();
    } catch (error) {
      if (error instanceof ApiError && error.code === CAVER_UNKNOWN) {
        // The row lets go of the entry it was refused for, and keeps its text. A row means its
        // entry for as long as it still reads the name it came under, and nothing about a
        // refusal changes what it reads — so left alone it would send the same dead entry on
        // every further press, and typing the name again, which is what the message suggests,
        // would change nothing. From here the text is a name like any typed one: saved as one,
        // or replaced by choosing somebody out of the list, which this refusal has just had
        // read again. The line under the box says which of the two it now is.
        //
        // Only where the row still holds that entry. The fields stay live while a save is on
        // its way, and somebody chosen in the meantime is not who the refusal was about.
        const row = form.getFieldValue('person') as CaverReferenceRow | undefined;
        if (row && row.caverId === body.caverId) {
          form.setFieldValue('person', { name: row.name });
        }
        message.error(t('expeditions.stay.personGone'));
      } else {
        message.error(t('common.saveFailed'));
      }
    }
  };

  return (
    <Modal
      title={entry ? t('expeditions.stay.edit') : t('expeditions.stay.add')}
      open={open}
      onCancel={onClose}
      onOk={() => void onOk()}
      confirmLoading={saving}
      // Not to be put away while a save is on its way — by the cross, by Cancel, by Escape or
      // by a press outside it. The page keeps one dialog for every stay on the roster, and what
      // a save does when it is answered it does to the dialog as it then stands: put away and
      // opened again on another stay in the meantime, that one would be closed under whoever
      // was typing in it by the first save's answer, or told that a person it never named is
      // gone. The wait is the write and the roster read back after it.
      closable={!saving}
      keyboard={!saving}
      mask={{ closable: !saving }}
      cancelButtonProps={{ disabled: saving }}
      destroyOnHidden
    >
      <Typography.Paragraph type="secondary">{t('expeditions.stay.hint')}</Typography.Paragraph>
      <Form<FormValues> form={form} layout="vertical">
        <Form.Item
          name="person"
          label={t('expeditions.stay.person')}
          required
          rules={[
            {
              // The field's value is a row, never empty in itself, so "required" has to look at
              // the text the row carries.
              validator: (_, value?: CaverReferenceRow) =>
                value && value.name.trim().length > 0
                  ? Promise.resolve()
                  : Promise.reject(new Error(t('expeditions.stay.personRequired'))),
            },
          ]}
        >
          <CaverNameField
            placeholder={t('expeditions.stay.personPlaceholder')}
            data-testid="expedition-stay-person"
          />
        </Form.Item>
        <Form.Item
          name="roleId"
          label={t('expeditions.stay.role')}
          rules={[{ required: true, message: t('expeditions.stay.roleRequired') }]}
        >
          <Select
            data-testid="expedition-stay-role"
            options={(roles ?? []).map((role) => ({
              value: role.id,
              label: expeditionRosterRoleLabel(role, t),
            }))}
          />
        </Form.Item>
        <Form.Item
          name="dates"
          label={t('expeditions.dates')}
          extra={t('expeditions.stay.daysHint')}
          rules={[{ required: true }]}
        >
          <RangePicker style={{ width: '100%' }} allowClear={false} />
        </Form.Item>
        <Form.Item name="note" label={t('expeditions.stay.note')}>
          <Input
            maxLength={NOTE_MAX}
            placeholder={t('expeditions.stay.notePlaceholder')}
            data-testid="expedition-stay-note"
          />
        </Form.Item>
      </Form>
    </Modal>
  );
}
