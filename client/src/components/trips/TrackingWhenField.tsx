// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button, DatePicker, Flex, Form } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useTranslation } from 'react-i18next';
import './TrackingWhenField.css';

/**
 * The gaps worth a button of their own, in minutes back from now.
 *
 * <b>Chosen from what a relayed report actually is.</b> Word comes out of a cave by somebody
 * walking out with it, by a radio relay, or by a phone call that reaches the surface late, and the
 * gap between the saying and the writing down is minutes to a couple of hours. Below five minutes
 * the exact moment does not change anything anybody reads; beyond two hours the reader knows the
 * clock time they were told and the picker is the right way to say it.
 *
 * All of them are in the past, which matters: the server refuses a moment more than a couple of
 * minutes ahead of its own clock, so a quick answer can never be the thing that gets a report
 * turned away.
 */
const OFFSETS_MINUTES = [5, 15, 30, 60, 120] as const;

/**
 * The moment a quick answer names, counted from the instant it is pressed.
 *
 * Kept to this file rather than exported: what a test has to hold this to is the instant that
 * reaches the request, which is read off the report body, and a test of this function alone would
 * pass while the button beside it wrote the value somewhere nothing sends.
 */
function offsetInstant(minutesAgo: number): Dayjs {
  return dayjs().subtract(minutesAgo, 'minute');
}

/**
 * Puts the field somewhere the enlarged calendar has room to open.
 *
 * <b>Sizing the calendar for a finger is what stops it fitting, so the same branch that enlarges it
 * has to make room for it.</b> Six weeks of forty-pixel days, a column of hours and a footer come to
 * a panel around 730px tall. The library anchors it to the field and puts it above or below —
 * whichever side has more room — and takes neither side's *size* into account beyond that, so a
 * field halfway down a phone screen has about four hundred pixels either way and the panel is placed
 * with three hundred of it off the top of the screen. Measured on a 412x839 phone with the field at
 * y=432: the panel was drawn at y=-300, which is the month, the year and every arrow that walks
 * backwards through time gone off the top edge. The stylesheet's cap keeps the panel inside the
 * screen's height; this is what keeps it inside the screen at all.
 *
 * Only ever on a coarse pointer, and that is a statement about cause rather than a guess about the
 * device: on a mouse the calendar is the library's own size, fits beside the field wherever the
 * field is, and a page that jumped when a date picker opened would be a defect.
 *
 * The element scrolled is whatever has the keyboard, which at this moment is the picker's own input
 * — the panel opens because that input was focused. Nothing is looked up, and nothing here knows
 * which scroll container the page is built from: the tracking tab scrolls inside the layout's
 * content area rather than in the window, and `scrollIntoView` is the one way to say "put this at
 * the top" that does not have to know that.
 */
function makeRoomForPanel(): void {
  const focused = document.activeElement;
  if (!(focused instanceof HTMLElement)) {
    return;
  }
  // Anything already near the top has room below it and is left where it is — a page that scrolls
  // when it did not need to is the same surprise as one that does not scroll when it did.
  if (focused.getBoundingClientRect().bottom <= 160) {
    return;
  }
  focused.scrollIntoView({ block: 'start' });
}

interface Props {
  /** How big everything here is drawn — decided by the pointer, by whatever card holds this. */
  size: 'large' | 'middle';
  /** Whether the calendar has to be made room for when it opens. The same answer, read once. */
  coarse: boolean;
  /**
   * Whether this field sits somewhere that cannot be scrolled far enough to open a calendar under.
   *
   * <b>Set by the surface, because only the surface knows how much scroll it has.</b> The card under
   * the watch scrolls with the page, so {@link makeRoomForPanel} can put the field at the top of the
   * screen and the panel opens below it with room to spare — measured on a 412x839 phone, the panel
   * lands at y=83 and ends at 815, entirely on screen. The dialog cannot: its fields scroll inside
   * the modal's body, which is capped so that the buttons that finish the report never leave the
   * screen, and that cap leaves the body 197px of scroll in total. Measured there, the field cannot
   * rise above y=353 however far it is scrolled, the panel opens at y=391, and the footer holding
   * "Now" and the "OK" that is the only way to commit a time sits at 1086–1133 — 291px below a
   * viewport 839px tall, with no scroll anywhere on the page that moves it.
   *
   * Where that is true the panel is anchored to the screen instead of to the field, which is what
   * the stylesheet does with it. Only ever on a coarse pointer: on a mouse the panel is the
   * library's own size and fits beside the field wherever the field is.
   */
  confined?: boolean;
  /**
   * What this field's controls are named by, so the two surfaces that draw it do not both answer
   * to one name. The card under the watch and the dialog opened from the model can be on screen at
   * once — the dialog opens over the tab the card is in — and a locator matching both picks
   * neither, which is a defect this application has met once already and named on the element.
   */
  idPrefix: string;
}

/**
 * When a report was said, and the four words that are almost always the answer.
 *
 * <b>The picker alone was a stopwatch problem dressed as a form field.</b> A relayed report is by
 * definition in the past, and naming a past moment through a date-and-time picker is four separate
 * acts — open it, land on today, scroll an hour column, scroll a minute column — done one-handed,
 * on a phone, while somebody is still on the line. What is actually being said is "about twenty
 * minutes ago", and the gap is what the reader knows; the clock time is something they would have
 * to work out. So the gap is what is offered, and the picker stays for the one report in ten that
 * names a time somebody wrote down.
 *
 * <b>"Now" fills the field in rather than emptying it.</b> Leaving it empty is the truer "now" —
 * the server stamps the report with its own clock — but a button that answers by making a field go
 * blank reads as a button that did nothing, and this one is pressed in a hurry. The instant it
 * writes is within the couple of minutes of skew the server allows, so the report lands. Emptying
 * it is still there: the picker clears from its own control, and an untouched field was never
 * filled in to begin with.
 *
 * The buttons are given as the field's own help rather than as a row beside it: they belong to this
 * question and nothing else, and a row of five at the width of a phone is a row that has to be
 * allowed to wrap under the control it answers for.
 */
export default function TrackingWhenField({ size, coarse, idPrefix, confined = false }: Props) {
  const { t } = useTranslation();
  // Read from the surrounding form rather than passed in, so the two cards that draw this cannot
  // hand it a form other than the one the field is registered on.
  const form = Form.useFormInstance<{ recordedAt?: Dayjs | null }>();

  const quick = (minutesAgo: number, label: string, testId: string) => (
    <Button
      key={testId}
      size={size}
      onClick={() => form.setFieldsValue({ recordedAt: offsetInstant(minutesAgo) })}
      data-testid={testId}
    >
      {label}
    </Button>
  );

  return (
    <Form.Item
      name="recordedAt"
      label={t('trips.tracking.reportAt')}
      extra={
        <>
          <Flex gap="small" wrap className="tracking-report-when-quick">
            {quick(0, t('trips.tracking.reportAtNow'), `${idPrefix}-when-now`)}
            {OFFSETS_MINUTES.map((minutes) =>
              quick(
                minutes,
                minutes < 60
                  ? t('trips.tracking.reportAtMinutesAgo', { minutes })
                  : t('trips.tracking.reportAtHoursAgo', { hours: minutes / 60 }),
                `${idPrefix}-when-${minutes}`,
              ),
            )}
          </Flex>
          {t('trips.tracking.reportAtHelp')}
        </>
      }
    >
      {/* <b>Named so the panel can be made to fit a phone.</b> It is drawn in a portal at the end of
          the document, so the only way to reach it is a class it carries; what the class does is in
          this component's stylesheet, where the geometry is. Left alone, the calendar and the
          columns of hours stand side by side and come to more than a phone is wide — measured at
          457px on a 412px screen, hanging 82px off the left edge with Sunday, Monday and both
          "previous month" arrows off it. A relayed report is by definition in the past, so a picker
          that cannot go back a month is a picker that cannot do the one job it is here for. */}
      <DatePicker
        showTime
        style={{ width: '100%' }}
        classNames={{
          popup: {
            root: `tracking-report-when-popup${confined ? ' tracking-report-when-popup-pinned' : ''}`,
          },
        }}
        onOpenChange={(open) => {
          // Nothing to make room by where the panel no longer follows the field: the scroll would
          // move the field a couple of hundred pixels, leave the panel where the screen pins it,
          // and read as a form that jumped for no reason.
          if (open && coarse && !confined) {
            makeRoomForPanel();
          }
        }}
        data-testid={`${idPrefix}-recorded-at`}
      />
    </Form.Item>
  );
}
