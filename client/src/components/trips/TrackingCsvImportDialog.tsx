// SPDX-License-Identifier: AGPL-3.0-or-later
import { useId, useMemo, useState, type Key, type ReactNode } from 'react';
import { DownloadOutlined } from '@ant-design/icons';
import {
  Alert,
  App,
  Button,
  Checkbox,
  Collapse,
  Descriptions,
  Flex,
  Form,
  Input,
  Modal,
  Segmented,
  Select,
  Statistic,
  Table,
  Tag,
  Typography,
  Upload,
} from 'antd';
import { useTranslation } from 'react-i18next';
import { useCoarsePointer } from '../../hooks/useCoarsePointer.ts';
import { useIsMobile } from '../../hooks/useIsMobile.ts';
import {
  useTrackingCsvCommit,
  useTrackingCsvFields,
  useTrackingCsvPreview,
  type TrackingCsvDiagnostic,
  type TrackingCsvOptions,
  type TrackingCsvPreview,
  type TrackingCsvPreviewRow,
} from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import { downloadFile } from '../../api/download.ts';
import { trackingProblemMessage } from './trackingProblems.ts';
import {
  decodeSheet,
  SHEET_DELIMITERS,
  SHEET_ENCODINGS,
  sniffDelimiter,
  type SheetDelimiter,
  type SheetDelimiterChoice,
  type SheetEncodingChoice,
} from './trackingCsvSheet.ts';
import {
  momentInSheetZone,
  ownSheetZone,
  SHEET_ZONE_AS_WRITTEN,
  sheetDayInWords,
  sheetZoneNames,
} from './trackingCsvZones.ts';
import './TrackingCsvImportDialog.css';

interface Props {
  tripLogId: string;
  /**
   * The trip's own date ("2026-09-12"), offered as the day a sheet of bare times was kept on.
   *
   * Offered, never assumed: it fills the field in when the sheet turns out to need a day, and it
   * is sent only once the reviewer has been shown that field and read the sheet again.
   */
  tripDay?: string | null;
  /**
   * The trip's teams, so that a report's team can be named where a row would replace one.
   *
   * A replacement writes the team where the sheet has a team column, an empty cell included, so
   * the team is part of what the reviewer is agreeing to change.
   */
  teams?: readonly { id: string; title: string }[];
  open: boolean;
  onClose: () => void;
}

/** Where the sheet's text comes from: a file chosen on this machine, or rows pasted in. */
type SheetSource = 'file' | 'paste';

/** The days the server will put a sheet of times on; the field's own bounds, so both agree. */
const EARLIEST_SHEET_DAY = '1900-01-01';
const LATEST_SHEET_DAY = '2200-12-31';

/** Which of a row's fields a hand-written mapping can point at a header. */
type FieldMapping = Record<string, string>;

/** The label key of each separator, since a tab cannot be shown as itself. */
const DELIMITER_LABELS: Record<SheetDelimiter, string> = {
  ',': 'comma',
  ';': 'semicolon',
  '\t': 'tab',
};

type DateOrderChoice = 'auto' | 'dayFirst' | 'monthFirst';

/**
 * Reading a coordinator's spreadsheet of reports onto the trip's log.
 *
 * <b>Two steps, and the first one writes nothing.</b> What a sheet means depends on choices the
 * reviewer makes — which header is the moment, whether a numeric date is day-first, which words
 * this club writes "went in" in — so the sheet is read again under the current choices every time,
 * and what comes back is a page of exactly what committing would do. A sheet somebody opened and
 * thought better of leaves the log as it was.
 *
 * <b>Overwriting is asked for, not defaulted.</b> The key a report is filed under is the person and
 * the instant, which is what makes re-importing a corrected sheet safe: the second run changes the
 * rows it corrected instead of doubling them. It is also the one act that silently rewrites
 * history, so the box is unticked and the reviewer ticks it.
 *
 * The sample sheet is offered rather than described. The fastest way to explain a column layout is
 * a file somebody can open in the spreadsheet they already use, and it is generated from the same
 * spellings the reader detects — so a template this screen offers is one this server can read.
 *
 * <b>On a phone the preview is a list of cards and the settings are full-width fields.</b> Five
 * columns of a preview do not fit across a phone, and a table told to keep its own overflow keeps
 * it by scrolling sideways — which is not the same as showing it: the outcome column, the one that
 * says whether a row overwrites, would be the one off the edge. How much room there is across is a
 * question about the width, so it is the width that decides; how big a control has to be is the
 * pointer's question and the library answers it on its own here.
 */
export default function TrackingCsvImportDialog({
  tripLogId,
  tripDay,
  teams,
  open,
  onClose,
}: Props) {
  const { t, i18n } = useTranslation();
  const narrow = useIsMobile();
  // Only the ticks on the preview's rows ask this: a bare checkbox is sixteen pixels under any
  // pointer, and the cell it sits in is what a finger has to be able to hit.
  const coarse = useCoarsePointer();
  // The application's own message surface rather than the static one: this dialog is rendered under
  // the application shell, whose theme and placement the static API does not see, and the static
  // one draws into a root of its own that outlives whatever mounted it.
  const { message } = App.useApp();
  // The file's bytes rather than its text, so that changing the encoding re-reads the same file
  // instead of asking for it again.
  const [bytes, setBytes] = useState<ArrayBuffer | null>(null);
  const [fileName, setFileName] = useState<string | null>(null);
  /**
   * Rows pasted in, as the other way of handing the sheet over.
   *
   * <b>For the phone a coordinator actually has in their hand.</b> A sheet kept in a spreadsheet
   * application on a phone is not a file anybody can find: getting it into a file chooser means
   * exporting it, saving it somewhere and finding it again, where selecting the rows and copying
   * them is two gestures. What a spreadsheet puts on the clipboard is the same rows separated by
   * tabs, which the reader takes like any other separator — so the pasted text goes through the
   * same read and the same import as a file's, with nothing of its own on the server.
   *
   * Both are kept while the dialog is open and only the chosen one is sent, so looking at the other
   * way in does not throw away a file already chosen or rows already pasted.
   */
  const [source, setSource] = useState<SheetSource>('file');
  const [pasted, setPasted] = useState('');
  const [encoding, setEncoding] = useState<SheetEncodingChoice>('auto');
  const [delimiter, setDelimiter] = useState<SheetDelimiterChoice>('auto');
  const [dateOrder, setDateOrder] = useState<DateOrderChoice>('auto');
  /**
   * Whose clock the sheet's times are on: a zone by name, or "exactly as written".
   *
   * <b>Opens on "exactly as written", every time.</b> That is the reading a sheet has always been
   * given here, so a sheet imported without touching this is imported as it was before the choice
   * existed. It is deliberately not remembered from one import to the next: a remembered zone is a
   * different default, applied to a sheet whose reviewer never chose it.
   */
  const [zone, setZone] = useState<string>(SHEET_ZONE_AS_WRITTEN);
  /**
   * The day a sheet of bare times was kept on, and whether the sheet has asked for one.
   *
   * <b>The question comes from the sheet, not from this screen.</b> Most sheets write their dates,
   * and a day field standing there for all of them would be a setting that does nothing. So the
   * field appears once a read has come back saying the times have no dates, already holding the
   * trip's own date — the likeliest answer — and the reviewer reads the sheet again to accept it.
   * A day nobody has been shown is never sent.
   *
   * Once asked it stays for that sheet, whatever later reads say: a field that came and went as
   * the columns were remapped would take the reviewer's answer away with it.
   */
  const [day, setDay] = useState(tripDay ?? '');
  const [asksForDay, setAsksForDay] = useState(false);
  const [replaceExisting, setReplaceExisting] = useState(false);
  const [mapping, setMapping] = useState<FieldMapping>({});
  const [wentIn, setWentIn] = useState('');
  const [cameOut, setCameOut] = useState('');
  const [preview, setPreview] = useState<TrackingCsvPreview | null>(null);
  /**
   * Whether the table on screen is a second reading, made because the first could not be imported.
   *
   * The server reads the sheet again for the write, against the trip as it is by then. Where that
   * would no longer write what was read here it writes nothing and says so, and the sheet is read
   * again at once — which would otherwise look like a button that did nothing but shuffle the
   * table. Kept until the reviewer reads the sheet themselves or it is imported.
   */
  const [readAgainAfterChange, setReadAgainAfterChange] = useState(false);
  /**
   * The lines of the sheet the reviewer has left ticked, every one of them after each read.
   *
   * <b>Lines, not rows.</b> A line naming two people is two rows of the preview and one line of the
   * sheet, and a line is what the import takes: it is committed whole or left out whole. Unticking
   * one person's row of it therefore unticks the line, and the table shows both rows going with it
   * rather than pretending half a line could be imported.
   */
  const [chosenLines, setChosenLines] = useState<ReadonlySet<number>>(() => new Set());
  // The header the sheet was last read with, kept apart from the preview because it outlives it:
  // the column choosers offer these names, and a chooser that lost its options the moment a
  // choice disarmed the preview could only ever be used once.
  const [header, setHeader] = useState<readonly string[]>([]);
  const [fetchingTemplate, setFetchingTemplate] = useState(false);
  /**
   * What ties each setting below to its label. None of them is a field the form holds — each is
   * the dialog's own state — and a form item with no field name gives its label no `for`, so a
   * screen reader announced every one of these as a bare "combobox". Minted per dialog rather than
   * written down, so nothing else on the page can share one.
   */
  const idBase = useId();
  const fieldId = (name: string) => `${idBase}-${name}`;

  const fields = useTrackingCsvFields();
  const read = useTrackingCsvPreview();
  const commit = useTrackingCsvCommit();

  const decoded = useMemo(() => (bytes ? decodeSheet(bytes, encoding) : null), [bytes, encoding]);
  const text = source === 'paste' ? pasted : (decoded?.text ?? '');
  // Guessed from the header line of the text as read, so a semicolon sheet — what a Romanian
  // Excel writes, and what it writes back after opening the comma template — is read as one
  // without anybody being told the sheet lacks columns it has.
  const sniffed = useMemo(() => sniffDelimiter(text), [text]);
  const effectiveDelimiter: SheetDelimiter = delimiter === 'auto' ? sniffed : delimiter;

  const options: TrackingCsvOptions = useMemo(() => {
    const columns = Object.fromEntries(
      Object.entries(mapping).filter(([, header]) => header.trim().length > 0),
    );
    const words = (value: string) =>
      value
        .split(',')
        .map((word) => word.trim())
        .filter((word) => word.length > 0);

    return {
      // Left out rather than sent empty: the reader treats a named field as "look for exactly this
      // and say so if it is missing", so naming a field with nothing would report every column of
      // the file as absent.
      columns: Object.keys(columns).length > 0 ? columns : null,
      delimiter: effectiveDelimiter,
      multiValueSeparators: null,
      dateOrder: dateOrder === 'auto' ? null : dateOrder,
      wentInWords: words(wentIn).length > 0 ? words(wentIn) : null,
      cameOutWords: words(cameOut).length > 0 ? words(cameOut) : null,
      timeZone: zone === SHEET_ZONE_AS_WRITTEN ? null : zone,
      day: asksForDay && day.length > 0 ? day : null,
    };
  }, [mapping, wentIn, cameOut, effectiveDelimiter, dateOrder, zone, asksForDay, day]);

  // The zones offered, the importer's own first: it is the answer for nearly every sheet, and
  // nobody should have to know how their own zone is spelled to find it in a list of hundreds.
  const ownZone = useMemo(ownSheetZone, []);
  const zoneOptions = useMemo(
    () => [
      { value: SHEET_ZONE_AS_WRITTEN, label: t('trips.tracking.csvImport.zoneAsWritten') },
      ...(ownZone
        ? [{ value: ownZone, label: t('trips.tracking.csvImport.zoneMine', { zone: ownZone }) }]
        : []),
      ...sheetZoneNames()
        .filter((name) => name !== ownZone)
        .map((name) => ({ value: name, label: name })),
    ],
    [ownZone, t],
  );
  // Asked of the browser rather than assumed: reading the clipboard exists only on a secure page
  // and not in every browser, and a button that can only fail is worse than a text box that
  // already takes a paste from the keyboard or a long press.
  const clipboardReadable =
    typeof navigator !== 'undefined' && typeof navigator.clipboard?.readText === 'function';

  /** Saving the sample sheet, through the one helper that knows how to carry the token. */
  const takeTemplate = async () => {
    setFetchingTemplate(true);
    try {
      await downloadFile('/api/v1/tracking-csv-import/template');
    } catch (error) {
      message.error(trackingProblemMessage(error, t));
    } finally {
      setFetchingTemplate(false);
    }
  };

  /**
   * A change to how the sheet is read takes the preview with it.
   *
   * <b>The preview is a page of exactly what committing would do, and that is only true under the
   * settings it was read with.</b> A reviewer who previews, then points the moment column elsewhere
   * or adds a word for "came out", and presses Import would otherwise commit rows the table never
   * showed — different kinds, different keys, different overwrites — because the commit reads the
   * sheet again under the current settings. So every setting the reading depends on disarms Import
   * until the sheet has been read again, and the table goes with it rather than standing there
   * describing a reading that is no longer the one on offer.
   */
  const rereadWith = <T,>(set: (value: T) => void) => (value: T) => {
    set(value);
    setPreview(null);
  };
  const chooseEncoding = rereadWith(setEncoding);
  const chooseDelimiter = rereadWith(setDelimiter);
  const chooseDateOrder = rereadWith(setDateOrder);
  const chooseZone = rereadWith(setZone);
  const chooseDay = rereadWith(setDay);
  const typeWentIn = rereadWith(setWentIn);
  const typeCameOut = rereadWith(setCameOut);
  const mapField = (field: string, headerName: string | undefined) => {
    setMapping((current) => ({ ...current, [field]: headerName ?? '' }));
    setPreview(null);
  };

  /**
   * Another sheet, by whichever way it came: what was decided about the last one goes with it.
   *
   * A tick to overwrite was given for the rows it was read on, the column choosers offer the last
   * sheet's headers, and the question about the day was that sheet's question. How the sheet is
   * read — separator, zone, words — stays, because those are usually the club's habits rather than
   * one sheet's.
   */
  const forgetSheet = () => {
    setPreview(null);
    setHeader([]);
    setReplaceExisting(false);
    setAsksForDay(false);
  };
  const chooseSource = (next: SheetSource) => {
    setSource(next);
    forgetSheet();
  };
  /** Typing or pasting is a change to the sheet itself, so the reading of it goes like any other. */
  const typePasted = (value: string) => {
    setPasted(value);
    setPreview(null);
    setReplaceExisting(false);
  };
  const pasteFromClipboard = async () => {
    try {
      typePasted(await navigator.clipboard.readText());
    } catch {
      // Refused by the browser or by the person asked: the box still takes a paste by hand, and
      // saying so is the whole of what there is to do about it.
      message.warning(t('trips.tracking.csvImport.pasteUnavailable'));
    }
  };

  const close = () => {
    setBytes(null);
    setFileName(null);
    setSource('file');
    setPasted('');
    setZone(SHEET_ZONE_AS_WRITTEN);
    setDay(tripDay ?? '');
    setAsksForDay(false);
    setEncoding('auto');
    setDelimiter('auto');
    setDateOrder('auto');
    setPreview(null);
    setReadAgainAfterChange(false);
    setChosenLines(new Set());
    setHeader([]);
    setReplaceExisting(false);
    setMapping({});
    setWentIn('');
    setCameOut('');
    onClose();
  };

  const look = async ({ afterChange = false }: { afterChange?: boolean } = {}) => {
    try {
      const page = await read.mutateAsync({ tripLogId, text, options });
      setReadAgainAfterChange(afterChange);
      setHeader(page.header);
      // Every line ticked afresh on each read: a new reading is a new set of rows, and a line left
      // unticked under the last one may now mean something else entirely.
      setChosenLines(new Set(page.rows.map((row) => row.line)));
      // The sheet saying its times have no dates — either by refusing to be read without a day, or
      // by having been read on the one that was named.
      if (
        page.day !== null ||
        page.fileDiagnostics.some((finding) => finding.problem === 'TimeColumnNeedsADay')
      ) {
        setAsksForDay(true);
      }
      setPreview(page);
    } catch (error) {
      message.error(trackingProblemMessage(error, t));
    }
  };

  const send = async () => {
    try {
      const done = await commit.mutateAsync({
        tripLogId,
        text,
        options,
        replaceExisting,
        // Always named, never left to mean "all": the lines the reviewer saw ticked are exactly the
        // lines committed, whatever the sheet turns out to hold when it is read again for the write.
        lines: [...chosenLines].sort((a, b) => a - b),
        // And the reading they were ticked on: the table is one reading of the sheet against the
        // trip, and the write is another, made later.
        planDigest: preview?.planDigest ?? null,
      });
      message.success(
        t('trips.tracking.csvImport.done', {
          created: done.created,
          updated: done.updated,
          unchanged: done.unchanged,
          skipped: done.skipped,
        }),
      );
      close();
    } catch (error) {
      if (error instanceof ApiError && error.code === 'tracking_csv.plan_changed') {
        // Nothing was written. The table no longer describes what importing would do, so it goes,
        // and so does the leave to overwrite — it was given for the rows of that reading, and the
        // new one may replace reports the last one would have added. Read again straight away
        // rather than left for the reviewer to ask for: the only way on is through a new reading.
        setPreview(null);
        setReplaceExisting(false);
        await look({ afterChange: true });
        return;
      }
      message.error(trackingProblemMessage(error, t));
    }
  };

  /**
   * Reading the chosen file here rather than uploading it.
   *
   * A tracking sheet is read, reviewed and committed in one sitting, so there is no review to
   * resume and nothing to be gained by storing the file — and the reviewer can change what its
   * columns mean, which a stored parse would be wrong about the moment they did.
   *
   * Read as bytes, not as text: a file's text depends on the encoding it was saved under, which
   * the browser's own reading assumes to be UTF-8 and a spreadsheet's plain "CSV" save often is
   * not. Decoding is done from the bytes each time the choice changes.
   */
  const takeFile = async (file: File) => {
    setBytes(await file.arrayBuffer());
    setFileName(file.name);
    // A tick to overwrite was given for the sheet it was read on. Another file is another set of
    // rows to overwrite, and the reviewer ticks it again for those.
    forgetSheet();
    return false;
  };

  const rows = preview?.rows ?? [];
  const rowKeyOf = (row: TrackingCsvPreviewRow) => `${row.line}-${row.caverId}`;
  const chosenRows = rows.filter((row) => chosenLines.has(row.line));
  // What importing would do now, counted from the rows still ticked — the figures above the table
  // are the promise Import makes, so they follow the ticks rather than the whole sheet.
  const chosenCreates = chosenRows.filter((row) => !row.replaces).length;
  const chosenReplaces = chosenRows.filter((row) => row.replaces).length;

  /**
   * A change of ticks, turned into lines.
   *
   * Each row whose tick changed takes its whole line with it, in the direction it changed: that is
   * what makes unticking one person of a two-person line untick the other too, where reading the
   * ticks that remain would have kept the line because its other row was still ticked.
   */
  const chooseRows = (keys: Key[]) => {
    const ticked = new Set(keys);
    const next = new Set(chosenLines);
    for (const row of rows) {
      const was = chosenLines.has(row.line);
      const is = ticked.has(rowKeyOf(row));
      if (is && !was) {
        next.add(row.line);
      } else if (was && !is) {
        next.delete(row.line);
      }
    }
    setChosenLines(next);
  };
  // Everything the reader found, in one fold: what was wrong with the file, which rows it would
  // not take, and what it noticed about the rows it would — a row's own findings are shown beside
  // the row as well, but the count on the fold is the count of things worth reading, and a word
  // that changed a row's meaning is one of them.
  const problems = [
    ...(preview?.fileDiagnostics ?? []),
    ...(preview?.refused ?? []),
    ...rows.flatMap((row) => row.diagnostics),
  ];

  /**
   * The findings that change what a row means, as against those that merely describe it.
   *
   * A state word the reader did not know is a row filed as "still at a place" when the sheet said
   * somebody had come out; a place dropped under a state is a row whose station was thrown away.
   * Both are what a reviewer is here to catch, and they are coloured apart from a ragged row or a
   * team name the trip lacks, which the row survived intact.
   */
  const changesMeaning = (problem: string) =>
    problem === 'StateWordUnknown' ||
    problem === 'StateOverridesPlace' ||
    // The two where the moment itself may be the wrong one: an hour the clocks showed twice, read
    // as the first of the two, and a time that only makes sense on the day after the one named.
    problem === 'MomentRepeatedByClockChange' ||
    problem === 'ClockRunsBackwards';

  /**
   * The moment a row is filed at, in the application's language.
   *
   * On the clocks of the zone the sheet was read in, where it was read in one — the zone the server
   * says it applied, not the one the chooser is on now — so the reviewer reads back the hour the
   * sheet wrote. A sheet read exactly as written is shown on the reader's own clock, like every
   * other moment on the tracking surfaces.
   */
  const momentOf = (row: TrackingCsvPreviewRow) =>
    momentInSheetZone(row.recordedAt, i18n.language, preview?.timeZone ?? null);
  const caverOf = (row: TrackingCsvPreviewRow) => (
    <Flex vertical>
      <span>{row.caverMatched}</span>
      {/* The written name beside the matched one wherever they differ, because that is the whole
          of what a reviewer is checking: "Ion" matching one person is right until the day two
          people on a trip answer to it. */}
      {row.caverWritten !== row.caverMatched && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t('trips.tracking.csvImport.writtenAs', { written: row.caverWritten })}
        </Typography.Text>
      )}
    </Flex>
  );
  /**
   * Where a row puts somebody, in the sheet's words and the log's.
   *
   * A place the sheet named is shown with the station it became, because the two are in different
   * vocabularies — the club's name for the place and the survey's name for the station — and a
   * reviewer shown only the station cannot tell whether the name they wrote was understood.
   */
  const placeOf = (row: TrackingCsvPreviewRow) =>
    row.stationName && row.placeLabel
      ? t('trips.tracking.csvImport.placeNamed', {
          label: row.placeLabel,
          station: row.stationName,
        })
      : (row.stationName ??
        (row.depthM !== null && row.depthM !== undefined
          ? t('caves.depthPlaces.metres', { depth: row.depthM })
          : t(`trips.tracking.kinds.${row.kind}`)));
  /**
   * One report in a line: what kind it is, where, and what was noted.
   *
   * The report in the log and the report the sheet would leave there are both said through this,
   * so the two lines of a row that replaces something differ only where the reports do.
   *
   * A report that claims a place and arrives with none is one whose place this account may not be
   * told: the server takes it out rather than the page hiding it. That is said in so many words —
   * a blank there would read as a report that never had a place, and the reviewer is about to
   * overwrite it.
   */
  const reportInWords = (report: {
    kind: TrackingCsvPreviewRow['kind'];
    place: string | null;
    depthM: number | string | null | undefined;
    team: string | null;
    note: string | null | undefined;
  }) => {
    const claimsPlace = report.kind === 'atStation' || report.kind === 'atDepth';
    const depth =
      report.depthM !== null && report.depthM !== undefined
        ? t('caves.depthPlaces.metres', { depth: report.depthM })
        : null;
    const where = claimsPlace
      ? [report.place, depth].filter((part) => part !== null).join(', ') ||
        t('trips.tracking.positionWithheld')
      : null;
    return [
      t(`trips.tracking.kinds.${report.kind}`),
      where,
      report.team,
      report.note ? t('trips.tracking.csvImport.noteQuoted', { note: report.note }) : null,
    ]
      .filter((part) => part !== null)
      .join(' · ');
  };
  /**
   * The place of a row — and, for a row that replaces a report, the report as it stands beside the
   * report as it would be left.
   *
   * "Replaces" alone asks the reviewer to take the overwrite on trust; the two lines are what they
   * are agreeing to.
   */
  const placeAndChangeOf = (row: TrackingCsvPreviewRow) => {
    const before = row.before;
    if (!row.replaces || !before) return placeOf(row);
    // What the import would leave, not what the sheet says: a replacement writes the team and the
    // note only where the sheet has a column for them, so without one the report keeps its own —
    // and the line has to show it kept, or a note left standing reads exactly like a note erased.
    const columns = preview?.resolvedColumns ?? {};
    const teamAfter = 'Team' in columns ? (row.teamId ?? null) : (before.teamId ?? null);
    const noteAfter = 'Note' in columns || 'Details' in columns ? row.note : before.note;
    // A team is named on both lines or on neither: where only one of the two reports has one, the
    // other says so, because a team taken away is a change like any other.
    const teamInWords = (teamId: string | null) =>
      teamId !== null
        ? (teams?.find((team) => team.id === teamId)?.title ?? t('trips.tracking.reportTeam'))
        : (before.teamId ?? null) !== null || teamAfter !== null
          ? t('trips.tracking.reportTeamNone')
          : null;
    return (
      <Flex vertical gap={2} data-testid={`trip-tracking-csv-row-change-${row.line}`}>
        <span data-testid={`trip-tracking-csv-row-before-${row.line}`}>
          <Typography.Text type="secondary">
            {t('trips.tracking.csvImport.beforeLabel')}
          </Typography.Text>{' '}
          {reportInWords({
            kind: before.kind,
            place: before.stationName ?? null,
            depthM: before.depthEnteredM,
            team: teamInWords(before.teamId ?? null),
            note: before.note,
          })}
        </span>
        <span data-testid={`trip-tracking-csv-row-after-${row.line}`}>
          <Typography.Text type="secondary">
            {t('trips.tracking.csvImport.afterLabel')}
          </Typography.Text>{' '}
          {reportInWords({
            kind: row.kind,
            place: row.stationName ? placeOf(row) : null,
            depthM: row.depthM,
            team: teamInWords(teamAfter),
            note: noteAfter,
          })}
        </span>
      </Flex>
    );
  };
  // What the reader noticed about this row, beside the row: a word it did not know, a place it
  // dropped, a team the trip lacks. Worded, never named, and coloured where the finding changed
  // what the row means.
  const findingsOf = (row: TrackingCsvPreviewRow) =>
    row.diagnostics.length === 0 ? null : (
      <Flex vertical gap={4} data-testid={`trip-tracking-csv-row-findings-${row.line}`}>
        {row.diagnostics.map((finding, index) => (
          <Tag
            key={`${finding.problem}-${index}`}
            color={changesMeaning(finding.problem) ? 'gold' : undefined}
            style={{ whiteSpace: 'normal', marginInlineEnd: 0 }}
          >
            {t(`trips.tracking.csvImport.problemNames.${finding.problem}`, {
              defaultValue: finding.problem,
            })}
            {finding.detail ? ` — ${finding.detail}` : ''}
          </Tag>
        ))}
      </Flex>
    );
  const outcomeOf = (row: TrackingCsvPreviewRow) =>
    row.replaces ? (
      <Tag color="orange">{t('trips.tracking.csvImport.willReplace')}</Tag>
    ) : (
      <Tag color="green">{t('trips.tracking.csvImport.willCreate')}</Tag>
    );
  /** One labelled field of a stacked row — the column heading become its label. */
  const fact = (label: string, value: ReactNode) =>
    value === null ? null : (
      <div className="tracking-csv-stacked-fact" key={label}>
        <Typography.Text type="secondary" className="tracking-csv-stacked-label">
          {label}
        </Typography.Text>
        <span className="tracking-csv-stacked-value">{value}</span>
      </div>
    );

  /**
   * What settled the day-and-month reading, said beside the reading.
   *
   * A sheet whose every day is twelve or under cannot settle it, and then the reading is a choice —
   * the reviewer's, or the usual one when nobody made it — and the one control that changes it is
   * behind a fold. Saying which of those decided is what lets a reviewer tell a reading the file
   * proved from one that is only a default. Compared without case, because the name arrives as the
   * server spells it.
   */
  const dateOrderSettledBy = (source: string) => {
    switch (source.toLowerCase()) {
      case 'file':
        return t('trips.tracking.csvImport.datesByFile');
      case 'conflict':
        return t('trips.tracking.csvImport.datesConflict');
      default:
        return dateOrder === 'auto'
          ? t('trips.tracking.csvImport.datesByDefault')
          : t('trips.tracking.csvImport.datesByChoice');
    }
  };

  return (
    <Modal
      open={open}
      onCancel={close}
      // Left to the library on a phone, where it already caps a dialog to the screen; a fixed
      // nine hundred is what a desk has room for.
      width={narrow ? undefined : 900}
      className={`tracking-csv-dialog${narrow ? ' tracking-csv-dialog-narrow' : ''}`}
      title={t('trips.tracking.csvImport.title')}
      data-testid="trip-tracking-csv-import"
      footer={
        <Flex justify="space-between" align="center" gap={12} wrap>
          {/* Fetched with the caller's token and saved from a blob, not opened as a link.
              The route is authenticated like every other, and plain anchor navigation carries
              no Authorization header — so an href here downloads a 401 body under a .csv name,
              which is worse than no button: the reviewer gets a file, opens it, and finds a
              problem document where the sample was meant to be. */}
          <Button
            icon={<DownloadOutlined />}
            loading={fetchingTemplate}
            onClick={() => void takeTemplate()}
            data-testid="trip-tracking-csv-template"
          >
            {t('trips.tracking.csvImport.template')}
          </Button>
          <Flex gap={8}>
            <Button onClick={close}>{t('common.cancel')}</Button>
            <Button
              onClick={() => void look()}
              loading={read.isPending}
              disabled={text.trim().length === 0}
              data-testid="trip-tracking-csv-preview"
            >
              {t('trips.tracking.csvImport.look')}
            </Button>
            <Button
              type="primary"
              onClick={() => void send()}
              loading={commit.isPending}
              // Out of reach with nothing ticked. The server reads an empty list as "none" and would
              // write nothing, so a press here could only be answered with a success that did
              // nothing — and a reviewer who unticked everything has nothing to import.
              disabled={!preview || chosenRows.length === 0}
              data-testid="trip-tracking-csv-commit"
            >
              {t('trips.tracking.csvImport.commit')}
            </Button>
          </Flex>
        </Flex>
      }
    >
      <Typography.Paragraph type="secondary">
        {t('trips.tracking.csvImport.help')}
      </Typography.Paragraph>
      {/* Said before a file is chosen: somebody on a telephone with a party underground should
          learn here, not three steps in, that this is the slow way to file one report. */}
      <Typography.Paragraph type="secondary" data-testid="trip-tracking-csv-desk-task">
        {t('trips.tracking.csvImport.deskTask')}
      </Typography.Paragraph>

      <Segmented<SheetSource>
        // The whole width on a phone, so each of the two is a target a thumb can hit.
        block={narrow}
        value={source}
        onChange={chooseSource}
        aria-label={t('trips.tracking.csvImport.source')}
        data-testid="trip-tracking-csv-source"
        style={{ marginBottom: 8 }}
        options={[
          { value: 'file', label: t('trips.tracking.csvImport.sourceFile') },
          { value: 'paste', label: t('trips.tracking.csvImport.sourcePaste') },
        ]}
      />

      {source === 'file' ? (
        <Upload.Dragger
          accept=".csv,text/csv"
          maxCount={1}
          beforeUpload={takeFile}
          showUploadList={false}
          data-testid="trip-tracking-csv-file"
        >
          <Typography.Text>
            {fileName ?? t('trips.tracking.csvImport.choose')}
          </Typography.Text>
          {/* What was made of the file, beside its name and outside the fold: a reviewer looking
              at a garbled name needs to know which encoding was guessed without going looking for
              the control that changes it. */}
          {decoded && (
            <div>
              <Typography.Text type="secondary" data-testid="trip-tracking-csv-read-as">
                {t('trips.tracking.csvImport.readAs', {
                  encoding: t(`trips.tracking.csvImport.encodings.${decoded.encoding}`),
                  delimiter: t(
                    `trips.tracking.csvImport.delimiters.${DELIMITER_LABELS[effectiveDelimiter]}`,
                  ),
                })}
              </Typography.Text>
            </div>
          )}
        </Upload.Dragger>
      ) : (
        <Form layout="vertical">
          <Form.Item
            label={t('trips.tracking.csvImport.pasteLabel')}
            htmlFor={fieldId('paste')}
            style={{ marginBottom: 0 }}
            extra={
              <Flex gap={12} align="center" wrap style={{ marginTop: 8 }}>
                {clipboardReadable && (
                  <Button
                    onClick={() => void pasteFromClipboard()}
                    data-testid="trip-tracking-csv-paste-clipboard"
                  >
                    {t('trips.tracking.csvImport.pasteFromClipboard')}
                  </Button>
                )}
                {/* The separator that was found, said as it is beside a file's name: pasted rows
                    have no encoding to guess, but which character divides them is still a guess
                    somebody may have to overrule. */}
                {pasted.trim().length > 0 && (
                  <span data-testid="trip-tracking-csv-pasted-as">
                    {t('trips.tracking.csvImport.pastedAs', {
                      delimiter: t(
                        `trips.tracking.csvImport.delimiters.${DELIMITER_LABELS[effectiveDelimiter]}`,
                      ),
                    })}
                  </span>
                )}
              </Flex>
            }
          >
            <Input.TextArea
              id={fieldId('paste')}
              value={pasted}
              onChange={(event) => typePasted(event.target.value)}
              placeholder={t('trips.tracking.csvImport.pastePlaceholder')}
              rows={6}
              // A sheet's names and stations are not prose: a phone "correcting" one on the way in
              // changes who a row is about.
              spellCheck={false}
              autoCorrect="off"
              autoCapitalize="off"
              data-testid="trip-tracking-csv-paste"
            />
          </Form.Item>
        </Form>
      )}

      {asksForDay && (
        // Outside the folds, where the question is seen: the sheet cannot be placed without the
        // answer, and a field behind "File settings" would leave the reviewer looking at a
        // refusal with nothing on screen to answer it with.
        <Alert
          type="info"
          showIcon
          style={{ marginTop: 12 }}
          data-testid="trip-tracking-csv-day-ask"
          title={t('trips.tracking.csvImport.sheetDayTitle')}
          description={
            <Form layout="vertical">
              <Form.Item
                label={t('trips.tracking.csvImport.sheetDay')}
                htmlFor={fieldId('day')}
                className="tracking-csv-field"
                style={{ marginBottom: 0 }}
                extra={t('trips.tracking.csvImport.sheetDayHelp')}
              >
                {/* The browser's own date field: a wheel on a phone, typed digits on a desk, and
                    nothing to open that the dialog would have to make room for. */}
                <Input
                  id={fieldId('day')}
                  type="date"
                  value={day}
                  min={EARLIEST_SHEET_DAY}
                  max={LATEST_SHEET_DAY}
                  onChange={(event) => chooseDay(event.target.value)}
                  data-testid="trip-tracking-csv-day"
                />
              </Form.Item>
            </Form>
          }
        />
      )}

      <Collapse
        ghost
        style={{ marginTop: 8 }}
        items={[
          {
            key: 'shape',
            label: t('trips.tracking.csvImport.shape'),
            children: (
              <>
                <Typography.Paragraph type="secondary">
                  {t('trips.tracking.csvImport.shapeHelp')}
                </Typography.Paragraph>
                <Form layout="vertical" size="small">
                  <Flex gap={12} wrap>
                    {/* A question about a file's bytes. Pasted rows arrive as text already, so
                        there is nothing for the choice to change and it is not offered. */}
                    {source === 'file' && (
                      <Form.Item
                        label={t('trips.tracking.csvImport.encoding')}
                        htmlFor={fieldId('encoding')}
                        className="tracking-csv-field"
                      >
                        <Select<SheetEncodingChoice>
                          id={fieldId('encoding')}
                          value={encoding}
                          onChange={chooseEncoding}
                          data-testid="trip-tracking-csv-encoding"
                          options={[
                            { value: 'auto', label: t('trips.tracking.csvImport.encodingAuto') },
                            ...SHEET_ENCODINGS.map((value) => ({
                              value,
                              label: t(`trips.tracking.csvImport.encodings.${value}`),
                            })),
                          ]}
                        />
                      </Form.Item>
                    )}
                    <Form.Item
                      label={t('trips.tracking.csvImport.delimiter')}
                      htmlFor={fieldId('delimiter')}
                      className="tracking-csv-field"
                    >
                      <Select<SheetDelimiterChoice>
                        id={fieldId('delimiter')}
                        value={delimiter}
                        onChange={chooseDelimiter}
                        data-testid="trip-tracking-csv-delimiter"
                        options={[
                          { value: 'auto', label: t('trips.tracking.csvImport.delimiterAuto') },
                          ...SHEET_DELIMITERS.map((value) => ({
                            value,
                            label: t(`trips.tracking.csvImport.delimiters.${DELIMITER_LABELS[value]}`),
                          })),
                        ]}
                      />
                    </Form.Item>
                    <Form.Item
                      label={t('trips.tracking.csvImport.dateOrder')}
                      htmlFor={fieldId('date-order')}
                      className="tracking-csv-field"
                    >
                      <Select<DateOrderChoice>
                        id={fieldId('date-order')}
                        value={dateOrder}
                        onChange={chooseDateOrder}
                        data-testid="trip-tracking-csv-date-order"
                        options={[
                          { value: 'auto', label: t('trips.tracking.csvImport.dateOrderAuto') },
                          { value: 'dayFirst', label: t('trips.tracking.csvImport.dateOrderDayFirst') },
                          {
                            value: 'monthFirst',
                            label: t('trips.tracking.csvImport.dateOrderMonthFirst'),
                          },
                        ]}
                      />
                    </Form.Item>
                    <Form.Item
                      label={t('trips.tracking.csvImport.zone')}
                      htmlFor={fieldId('zone')}
                      className="tracking-csv-field tracking-csv-field-wide"
                      extra={t('trips.tracking.csvImport.zoneHelp')}
                    >
                      <Select<string>
                        id={fieldId('zone')}
                        showSearch
                        optionFilterProp="label"
                        value={zone}
                        onChange={chooseZone}
                        data-testid="trip-tracking-csv-zone"
                        options={zoneOptions}
                      />
                    </Form.Item>
                  </Flex>
                </Form>
              </>
            ),
          },
          {
            key: 'columns',
            label: t('trips.tracking.csvImport.columns'),
            children: (
              <>
                <Typography.Paragraph type="secondary">
                  {t('trips.tracking.csvImport.columnsHelp')}
                </Typography.Paragraph>
                <Form layout="vertical" size="small">
                  <Flex gap={12} wrap>
                    {(fields.data ?? []).map((field) => (
                      <Form.Item
                        key={field.field}
                        label={t(`trips.tracking.csvImport.fields.${field.field}`)}
                        htmlFor={fieldId(`map-${field.field}`)}
                        className="tracking-csv-field"
                      >
                        <Select
                          id={fieldId(`map-${field.field}`)}
                          allowClear
                          showSearch
                          value={mapping[field.field] || undefined}
                          placeholder={t('trips.tracking.csvImport.detected')}
                          data-testid={`trip-tracking-csv-map-${field.field}`}
                          onChange={(headerName: string | undefined) =>
                            mapField(field.field, headerName)
                          }
                          options={header.map((name) => ({ value: name, label: name }))}
                        />
                      </Form.Item>
                    ))}
                  </Flex>
                  <Flex gap={12} wrap>
                    <Form.Item
                      label={t('trips.tracking.csvImport.wentInWords')}
                      htmlFor={fieldId('went-in')}
                      className="tracking-csv-field tracking-csv-field-wide"
                    >
                      <Input
                        id={fieldId('went-in')}
                        value={wentIn}
                        onChange={(event) => typeWentIn(event.target.value)}
                        placeholder={t('trips.tracking.csvImport.wentInPlaceholder')}
                        aria-describedby={fieldId('state-words-help')}
                      />
                    </Form.Item>
                    <Form.Item
                      label={t('trips.tracking.csvImport.cameOutWords')}
                      htmlFor={fieldId('came-out')}
                      className="tracking-csv-field tracking-csv-field-wide"
                    >
                      <Input
                        id={fieldId('came-out')}
                        value={cameOut}
                        onChange={(event) => typeCameOut(event.target.value)}
                        placeholder={t('trips.tracking.csvImport.cameOutPlaceholder')}
                        aria-describedby={fieldId('state-words-help')}
                      />
                    </Form.Item>
                  </Flex>
                  {/* Said beside the two fields because both of their surprises are invisible from
                      them: a word typed here replaces the usual list for its side rather than
                      adding to it, and a sheet's "iesire" matches a typed "ieșire". */}
                  <Typography.Text
                    type="secondary"
                    id={fieldId('state-words-help')}
                    data-testid="trip-tracking-csv-state-words-help"
                  >
                    {t('trips.tracking.csvImport.stateWordsHelp')}
                  </Typography.Text>
                </Form>
              </>
            ),
          },
        ]}
      />

      {preview && (
        <>
          {readAgainAfterChange && (
            <Alert
              type="warning"
              showIcon
              style={{ marginTop: 8 }}
              data-testid="trip-tracking-csv-plan-changed"
              title={t('trips.tracking.csvImport.planChangedTitle')}
              description={t('trips.tracking.problems.csvPlanChanged')}
            />
          )}
          <Flex gap={24} wrap style={{ marginTop: 8, marginBottom: 12 }}>
            <Statistic
              title={t('trips.tracking.csvImport.rowsRead')}
              value={preview.rowsRead}
            />
            <Statistic
              title={t('trips.tracking.csvImport.creates')}
              value={chosenCreates}
              data-testid="trip-tracking-csv-creates"
            />
            <Statistic
              title={t('trips.tracking.csvImport.replaces')}
              value={chosenReplaces}
              data-testid="trip-tracking-csv-replaces"
            />
          </Flex>
          {/* How the moments were read, said where the numbers are: whose clock a time with no
              zone was put on, and which number of a date is the day, were each decided for the
              whole file — things a reviewer would otherwise learn from a row that looks three
              hours wrong. The zone named is the one the server says it applied, so the sentence
              cannot describe a reading other than the one in the table under it. */}
          <Typography.Paragraph type="secondary" data-testid="trip-tracking-csv-moments-rule">
            {t(
              preview.timeZone
                ? 'trips.tracking.csvImport.momentsRuleZone'
                : 'trips.tracking.csvImport.momentsRule',
              {
                zone: preview.timeZone,
                order: t(
                  preview.dateOrder === 'monthFirst'
                    ? 'trips.tracking.csvImport.datesMonthFirst'
                    : 'trips.tracking.csvImport.datesDayFirst',
                ),
              },
            )}{' '}
            {dateOrderSettledBy(preview.dateOrderSource)}
            {preview.day && (
              <>
                {' '}
                <span data-testid="trip-tracking-csv-moments-day">
                  {t('trips.tracking.csvImport.momentsOnDay', {
                    day: sheetDayInWords(preview.day, i18n.language),
                  })}
                </span>
              </>
            )}
          </Typography.Paragraph>

          {preview.unmatchedCavers.length > 0 && (
            // Said once however many rows wrote the name, because a sheet naming somebody who is
            // not on the trip is a missing participant — one thing to go and fix — and a refusal
            // per row buries that under its own repetitions.
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              data-testid="trip-tracking-csv-unmatched"
              title={t('trips.tracking.csvImport.unmatchedTitle')}
              description={
                <>
                  <div>{t('trips.tracking.csvImport.unmatchedBody')}</div>
                  <Flex gap={4} wrap style={{ marginTop: 8 }}>
                    {preview.unmatchedCavers.map((name) => (
                      <Tag key={name}>{name}</Tag>
                    ))}
                  </Flex>
                </>
              }
            />
          )}

          {preview.unmappedColumns.length > 0 && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('trips.tracking.csvImport.unmappedTitle')}
              description={preview.unmappedColumns.join(', ')}
            />
          )}

          {chosenReplaces > 0 && (
            <Checkbox
              checked={replaceExisting}
              onChange={(event) => setReplaceExisting(event.target.checked)}
              style={{ marginBottom: 12 }}
              data-testid="trip-tracking-csv-replace"
            >
              {t('trips.tracking.csvImport.replaceExisting', { count: chosenReplaces })}
            </Checkbox>
          )}

          {rows.length > 0 && (
            <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
              {t('trips.tracking.csvImport.chooseRowsHelp')}
            </Typography.Paragraph>
          )}

          <Table<TrackingCsvPreviewRow>
            size="small"
            rowKey={rowKeyOf}
            dataSource={rows}
            rowSelection={{
              selectedRowKeys: chosenRows.map(rowKeyOf),
              onChange: chooseRows,
              // Wide enough for a fingertip; the stylesheet gives the tick the whole cell.
              columnWidth: coarse ? 48 : undefined,
              // Named by its line, because a line is what the tick takes or leaves.
              getCheckboxProps: (row) => ({
                'aria-label': t('trips.tracking.csvImport.chooseRow', { line: row.line }),
              }),
            }}
            pagination={{ pageSize: 10, size: 'small' }}
            // The headings go with the columns on a phone: with every field labelled in its row,
            // a row of headings would be a second set of the same words, on the axis there is
            // least of.
            showHeader={!narrow}
            className={narrow ? 'tracking-csv-rows-stacked' : undefined}
            data-testid="trip-tracking-csv-rows"
            columns={
              narrow
                ? [
                    {
                      title: t('trips.tracking.csvImport.line'),
                      key: 'row',
                      render: (_: unknown, row: TrackingCsvPreviewRow) => (
                        <div className="tracking-csv-stacked">
                          <div className="tracking-csv-stacked-head">
                            <Typography.Text strong>
                              {t('trips.tracking.csvImport.atLine', { line: row.line })}
                            </Typography.Text>
                            {/* Beside the line rather than down among the fields: whether a row
                                overwrites is what the reviewer is reading the preview for. */}
                            {outcomeOf(row)}
                          </div>
                          <div className="tracking-csv-stacked-facts">
                            {fact(t('trips.tracking.csvImport.moment'), momentOf(row))}
                            {fact(t('trips.tracking.csvImport.caver'), caverOf(row))}
                            {fact(t('trips.tracking.csvImport.place'), placeAndChangeOf(row))}
                            {fact(t('trips.tracking.csvImport.findings'), findingsOf(row))}
                          </div>
                        </div>
                      ),
                    },
                  ]
                : [
                    { title: t('trips.tracking.csvImport.line'), dataIndex: 'line', width: 60 },
                    {
                      title: t('trips.tracking.csvImport.moment'),
                      key: 'moment',
                      // Where the reviewer sees what hour each row lands on before anything
                      // is written: on the sheet's own clocks where a zone was named for it, on
                      // the reader's otherwise.
                      render: (_: unknown, row: TrackingCsvPreviewRow) => momentOf(row),
                    },
                    {
                      title: t('trips.tracking.csvImport.caver'),
                      key: 'caver',
                      render: (_: unknown, row: TrackingCsvPreviewRow) => caverOf(row),
                    },
                    {
                      title: t('trips.tracking.csvImport.place'),
                      key: 'place',
                      render: (_: unknown, row: TrackingCsvPreviewRow) => placeAndChangeOf(row),
                    },
                    {
                      title: t('trips.tracking.csvImport.findings'),
                      key: 'findings',
                      render: (_: unknown, row: TrackingCsvPreviewRow) => findingsOf(row),
                    },
                    {
                      title: t('trips.tracking.csvImport.outcome'),
                      key: 'outcome',
                      width: 120,
                      render: (_: unknown, row: TrackingCsvPreviewRow) => outcomeOf(row),
                    },
                  ]
            }
          />

          {problems.length > 0 && (
            <Collapse
              ghost
              style={{ marginTop: 8 }}
              items={[
                {
                  key: 'problems',
                  label: t('trips.tracking.csvImport.problems', { count: problems.length }),
                  children: (
                    <Descriptions
                      size="small"
                      column={1}
                      bordered
                      data-testid="trip-tracking-csv-problems"
                      items={problems.map((problem: TrackingCsvDiagnostic, index) => ({
                        key: `${problem.line}-${problem.problem}-${index}`,
                        label: t('trips.tracking.csvImport.atLine', { line: problem.line }),
                        children: (
                          <>
                            {t(`trips.tracking.csvImport.problemNames.${problem.problem}`, {
                              defaultValue: problem.problem,
                            })}
                            {problem.detail ? ` — ${problem.detail}` : ''}
                          </>
                        ),
                      }))}
                    />
                  ),
                },
              ]}
            />
          )}
        </>
      )}
    </Modal>
  );
}
