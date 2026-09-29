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
import './TrackingCsvImportDialog.css';

interface Props {
  tripLogId: string;
  open: boolean;
  onClose: () => void;
}

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
export default function TrackingCsvImportDialog({ tripLogId, open, onClose }: Props) {
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
  const [encoding, setEncoding] = useState<SheetEncodingChoice>('auto');
  const [delimiter, setDelimiter] = useState<SheetDelimiterChoice>('auto');
  const [dateOrder, setDateOrder] = useState<DateOrderChoice>('auto');
  const [replaceExisting, setReplaceExisting] = useState(false);
  const [mapping, setMapping] = useState<FieldMapping>({});
  const [wentIn, setWentIn] = useState('');
  const [cameOut, setCameOut] = useState('');
  const [preview, setPreview] = useState<TrackingCsvPreview | null>(null);
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
  const text = decoded?.text ?? '';
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
    };
  }, [mapping, wentIn, cameOut, effectiveDelimiter, dateOrder]);

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
  const typeWentIn = rereadWith(setWentIn);
  const typeCameOut = rereadWith(setCameOut);
  const mapField = (field: string, headerName: string | undefined) => {
    setMapping((current) => ({ ...current, [field]: headerName ?? '' }));
    setPreview(null);
  };

  const close = () => {
    setBytes(null);
    setFileName(null);
    setEncoding('auto');
    setDelimiter('auto');
    setDateOrder('auto');
    setPreview(null);
    setChosenLines(new Set());
    setHeader([]);
    setReplaceExisting(false);
    setMapping({});
    setWentIn('');
    setCameOut('');
    onClose();
  };

  const look = async () => {
    try {
      const page = await read.mutateAsync({ tripLogId, text, options });
      setHeader(page.header);
      // Every line ticked afresh on each read: a new reading is a new set of rows, and a line left
      // unticked under the last one may now mean something else entirely.
      setChosenLines(new Set(page.rows.map((row) => row.line)));
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
      });
      message.success(
        t('trips.tracking.csvImport.done', {
          created: done.created,
          updated: done.updated,
          skipped: done.skipped,
        }),
      );
      close();
    } catch (error) {
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
    setPreview(null);
    setHeader([]);
    // A tick to overwrite was given for the sheet it was read on. Another file is another set of
    // rows to overwrite, and the reviewer ticks it again for those.
    setReplaceExisting(false);
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
    problem === 'StateWordUnknown' || problem === 'StateOverridesPlace';

  /** The moment a row is filed at, in the application's language and the reader's zone. */
  const momentOf = (row: TrackingCsvPreviewRow) => new Date(row.recordedAt).toLocaleString(i18n.language);
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
  const placeOf = (row: TrackingCsvPreviewRow) =>
    row.stationName ??
    (row.depthM !== null && row.depthM !== undefined
      ? t('caves.depthPlaces.metres', { depth: row.depthM })
      : t(`trips.tracking.kinds.${row.kind}`));
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
              // Never sent with no lines: an empty list is how the server is told "every line", so a
              // reviewer who unticked everything must find Import out of reach, not a full import.
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
          {/* How the moments were read, said where the numbers are: a sheet's 09:40 is filed as
              09:40 UTC and shown in the reviewer's own zone, and which number of a date is the day
              was decided for the whole file — both are things a reviewer would otherwise learn
              from a row that looks three hours wrong. */}
          <Typography.Paragraph type="secondary" data-testid="trip-tracking-csv-moments-rule">
            {t('trips.tracking.csvImport.momentsRule', {
              order: t(
                preview.dateOrder === 'monthFirst'
                  ? 'trips.tracking.csvImport.datesMonthFirst'
                  : 'trips.tracking.csvImport.datesDayFirst',
              ),
            })}{' '}
            {dateOrderSettledBy(preview.dateOrderSource)}
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
                            {fact(t('trips.tracking.csvImport.place'), placeOf(row))}
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
                      // In the application's language and the reader's zone, like every other
                      // clock on the tracking surfaces: a sheet's 14:30 is filed as 14:30 UTC,
                      // and this is where the reviewer sees what hour that lands on before
                      // anything is written.
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
                      render: (_: unknown, row: TrackingCsvPreviewRow) => placeOf(row),
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
