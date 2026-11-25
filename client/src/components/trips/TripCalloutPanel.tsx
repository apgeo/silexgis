// SPDX-License-Identifier: AGPL-3.0-or-later
import { CheckCircleOutlined, ClockCircleOutlined, WarningOutlined } from '@ant-design/icons';
import { Alert, App, DatePicker, Descriptions, Flex, Form, Modal, Space, Tag } from 'antd';
import Button from '../Button.tsx';
import dayjs, { type Dayjs } from 'dayjs';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useArrangeTripCallout,
  useReadTripLogAgain,
  useStandDownTripCallout,
  type TripCalloutState,
} from '../../api/hooks.ts';
import { ApiError, isConcurrencyConflict, lastReadETag } from '../../api/client.ts';
import { useNow } from '../../hooks/useNow.ts';

interface Props {
  tripId: string;
  state: TripCalloutState;
  /** When the party said they would be out, as an instant. */
  expectedReturnAt: string | null;
  /** When the alarm goes off if nobody has said they are out. */
  calloutAlarmAt: string | null;
  /**
   * When the scheduled pass that watches for overdue parties last finished — null when none ever
   * has, and null as well when this trip is one no pass will look at. Never treated as decoration:
   * it is the only evidence a page has that the promise made by an armed check is being kept.
   */
  calloutLastCheckedAt: string | null;
  /** Whether this reader is on the trip, and so may say the party is out. */
  canStandDown: boolean;
  /** Whether this reader may change the trip, and so may arrange or call off the check. */
  canEdit: boolean;
}

/**
 * How far behind the last completed pass a check may be before the page says nobody has looked.
 *
 * It is generous on purpose, and it is not a free number: the interval between passes is an
 * operator setting, but the server keeps that setting under a ceiling of half this, precisely so
 * that a healthy installation cannot reach this figure. That is what makes the warning worth
 * believing the one time it appears — a warning every armed trip carries permanently is a warning
 * nobody reads.
 */
const STALE_AFTER_MS = 60 * 60 * 1000;

interface ArrangeForm {
  expectedReturnAt: Dayjs | null;
  calloutAlarmAt: Dayjs | null;
}

/**
 * What a trip says about the arrangement to notice if its party does not come back, and — for
 * whoever may change the trip — where that arrangement is made.
 *
 * The rule this component exists for is the last one in it: **an armed check whose watcher has
 * not run recently reads as unchecked, never as safe.** A page that draws "an alarm is set" and
 * stops there is making a promise on behalf of something it has not heard from — and the shape of
 * a callout is that silence is the good news, so a broken watcher and a party safely underground
 * look exactly alike from here. Saying when the check last ran is the whole difference, and
 * "never" is the strongest form of the warning rather than an absence worth glossing over.
 *
 * Arranging the check lives here rather than in the form that saves the whole trip, because it is
 * its own write with its own route: an ordinary edit that carried these two times would re-arm, on
 * every save, a check somebody had already stood down.
 */
export default function TripCalloutPanel({
  tripId,
  state,
  expectedReturnAt,
  calloutAlarmAt,
  calloutLastCheckedAt,
  canStandDown,
  canEdit,
}: Props) {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const standDown = useStandDownTripCallout();
  const arrange = useArrangeTripCallout();
  const readTripAgain = useReadTripLogAgain();
  const [arranging, setArranging] = useState(false);
  const [form] = Form.useForm<ArrangeForm>();

  const live = state === 'armed' || state === 'overdue';
  // Named as the states worth drawing rather than as the one worth hiding: a trip nobody
  // arranged a check for has nothing to say here, and so has a value this build has never heard
  // of. A panel about a callout that appears when there is no callout is noise on every trip in
  // the installation, which is how a real one stops being read.
  const arranged = live || state === 'stoodDown';

  // Measured against a clock that redraws the panel by itself: on a page left open and only
  // watched, nothing else would, and the hour passing is exactly the change that has to show.
  const now = useNow();
  const lastChecked = calloutLastCheckedAt ? new Date(calloutLastCheckedAt) : null;
  const unchecked =
    live && (lastChecked === null || now - lastChecked.getTime() > STALE_AFTER_MS);

  const when = (value: string | null) =>
    value ? new Date(value).toLocaleString(i18n.language) : '—';

  const onStandDown = async () => {
    try {
      await standDown.mutateAsync({ id: tripId });
      message.success(t('trips.calloutStoodDownSuccess'));
    } catch (error) {
      // No check is armed any more: there is nothing left to stand down, and trying again would be
      // refused the same way.
      if (error instanceof ApiError && error.code === 'trip_log.callout_not_armed') {
        message.info(t('trips.calloutNotArmed'));
      } else {
        message.error(t('trips.calloutStandDownFailed'));
      }
    }
  };

  // The version of the trip the dialog was filled from. The page behind it goes on reading the
  // trip — a return from the phone app used to ring the party is one such read — and each read
  // files a newer version. Sending that one would put the times the dialog opened with back over
  // whatever another coordinator set meanwhile, with nobody told; so the save names this one.
  const openedAtVersion = useRef<string | undefined>(undefined);

  const fill = (
    times: { expectedReturnAt: string | null; calloutAlarmAt: string | null },
    version: string | undefined,
  ) => {
    openedAtVersion.current = version;
    form.setFieldsValue({
      expectedReturnAt: times.expectedReturnAt ? dayjs(times.expectedReturnAt) : null,
      calloutAlarmAt: times.calloutAlarmAt ? dayjs(times.calloutAlarmAt) : null,
    });
  };

  const openArrange = () => {
    fill({ expectedReturnAt, calloutAlarmAt }, lastReadETag(`/api/v1/trip-logs/${tripId}`));
    setArranging(true);
  };

  const onArrange = async () => {
    let values: ArrangeForm;
    try {
      values = await form.validateFields();
    } catch {
      // The form is already showing why. Swallowed rather than rethrown so a refused validation
      // does not surface as an unhandled rejection in the browser, which is watched for.
      return;
    }

    const alarm = values.calloutAlarmAt ? values.calloutAlarmAt.toISOString() : null;
    try {
      await arrange.mutateAsync({
        id: tripId,
        expectedReturnAt: values.expectedReturnAt ? values.expectedReturnAt.toISOString() : null,
        calloutAlarmAt: alarm,
        ifMatch: openedAtVersion.current,
      });
      message.success(alarm ? t('trips.calloutSaved') : t('trips.calloutCleared'));
      setArranging(false);
    } catch (error) {
      if (isConcurrencyConflict(error)) {
        // Somebody else arranged this callout since the dialog opened, and the application's
        // conflict warning says so. The trip is read again and the dialog refilled with the times
        // as they now stand, under the version of that read, so what is saved next is a deliberate
        // change of those rather than of what was there before. The copy the page holds is the
        // one the refused save came from: refilled from it, the next save would be refused too.
        try {
          const { trip, version } = await readTripAgain(tripId);
          fill(trip, version);
        } catch {
          // The dialog keeps what was typed; the next save is refused again until a read lands.
          message.error(t('trips.calloutSaveFailed'));
        }
      } else {
        message.error(t('trips.calloutSaveFailed'));
      }
    }
  };

  // The one control offered on a trip nobody has arranged a check for, and only to somebody who
  // could act on it. Everything else on this panel would be a statement about a check that does
  // not exist.
  const arrangeButton = canEdit && (
    <Button
      icon={<ClockCircleOutlined />}
      onClick={openArrange}
      data-testid="trip-callout-arrange"
    >
      {arranged ? t('trips.calloutChange') : t('trips.calloutArrange')}
    </Button>
  );

  const dialog = (
    <Modal
      open={arranging}
      title={t('trips.calloutStatus')}
      okText={t('common.save')}
      cancelText={t('common.cancel')}
      confirmLoading={arrange.isPending}
      onOk={onArrange}
      onCancel={() => setArranging(false)}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" requiredMark={false}>
        <Form.Item name="expectedReturnAt" label={t('trips.expectedReturnAt')}>
          <DatePicker
            showTime
            style={{ width: '100%' }}
            data-testid="trip-callout-expected-return"
          />
        </Form.Item>
        <Form.Item
          name="calloutAlarmAt"
          label={t('trips.calloutAlarmAt')}
          extra={t('trips.calloutAlarmHelp')}
          rules={[
            {
              // Checked here as well as on the server, because an alarm set before the party is
              // even due back fires while they are still underground and on time — and the person
              // filling this in is the one who can still fix it.
              validator: (_rule, value: Dayjs | null) => {
                const back = form.getFieldValue('expectedReturnAt') as Dayjs | null;
                return value && back && value.isBefore(back)
                  ? Promise.reject(new Error(t('trips.calloutAlarmBeforeReturn')))
                  : Promise.resolve();
              },
            },
          ]}
        >
          <DatePicker showTime style={{ width: '100%' }} data-testid="trip-callout-alarm-at" />
        </Form.Item>
      </Form>
    </Modal>
  );

  if (!arranged) {
    if (!canEdit) {
      return null;
    }

    return (
      <Space orientation="vertical" size="small" style={{ width: '100%', marginBottom: 12 }}>
        <Flex justify="flex-start">{arrangeButton}</Flex>
        {dialog}
      </Space>
    );
  }

  return (
    <Space orientation="vertical" size="small" style={{ width: '100%', marginBottom: 12 }}>
      {state === 'overdue' && (
        <Alert
          type="error"
          showIcon
          icon={<WarningOutlined />}
          title={t('trips.calloutOverdueTitle')}
          description={t('trips.calloutOverdueBody')}
          data-testid="trip-callout-overdue"
        />
      )}

      {/* The warning that matters most, and the one it would be easiest to leave out. It is shown
          for an overdue check as well as an armed one: a check that fired and then stopped being
          watched is no more trustworthy than one that never fired. */}
      {unchecked && (
        <Alert
          type="warning"
          showIcon
          title={t('trips.calloutUncheckedTitle')}
          description={t('trips.calloutUncheckedBody')}
          data-testid="trip-callout-unchecked"
        />
      )}

      <Descriptions column={1} size="small" data-testid="trip-callout">
        <Descriptions.Item label={t('trips.calloutStatus')}>
          <Tag color={state === 'overdue' ? 'red' : state === 'armed' ? 'blue' : 'default'}>
            {t(`trips.calloutStateValues.${state}`)}
          </Tag>
        </Descriptions.Item>
        <Descriptions.Item label={t('trips.expectedReturnAt')}>
          {when(expectedReturnAt)}
        </Descriptions.Item>
        <Descriptions.Item label={t('trips.calloutAlarmAt')}>{when(calloutAlarmAt)}</Descriptions.Item>
        {live && (
          <Descriptions.Item label={t('trips.calloutLastChecked')}>
            <span data-testid="trip-callout-last-checked">
              {lastChecked ? lastChecked.toLocaleString(i18n.language) : t('trips.calloutNeverChecked')}
            </span>
          </Descriptions.Item>
        )}
      </Descriptions>

      {/* Standing down is open to the people on the trip and to nobody else — saying a party is
          out is a statement about where people are, not an edit to the record of the trip, so it
          is not offered to a reader who merely holds the right to change it. Changing the
          arrangement is the other way round, and the two sit side by side because on many trips
          one person holds both. */}
      <Flex justify="flex-start" gap="small">
        {live && canStandDown && (
          <Button
            type="primary"
            icon={<CheckCircleOutlined />}
            loading={standDown.isPending}
            onClick={onStandDown}
            data-testid="trip-callout-stand-down"
          >
            {t('trips.calloutStandDown')}
          </Button>
        )}
        {arrangeButton}
      </Flex>

      {dialog}
    </Space>
  );
}
