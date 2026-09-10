// SPDX-License-Identifier: AGPL-3.0-or-later
import { Card, Checkbox, Col, Collapse, Form, Input, Row, Select, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useCavingGroups,
  type TripCsvDateOrder,
  type TripCsvDateOrderSource,
  type TripCsvEncoding,
  type TripCsvEncodingSource,
  type TripCsvField,
  type TripImportOptions,
} from '../../api/hooks.ts';

/** Every column the reader can be pointed at, in the order a sheet usually writes them. */
const FIELDS: readonly TripCsvField[] = [
  'sourceId',
  'startDate',
  'endDate',
  'title',
  'country',
  'massif',
  'subArea',
  'caves',
  'proposers',
  'participants',
  'tripType',
  'details',
  'details2',
  'errors',
];

/**
 * The encodings a sheet may be read under, in the order somebody scanning the list would look
 * for them: what anything current writes, then the two code pages a Central European archive is
 * actually saved in, then the rest.
 */
const ENCODINGS: readonly TripCsvEncoding[] = [
  'utf8',
  'windows1250',
  'iso88592',
  'windows1252',
  'utf16Le',
  'utf16Be',
];

/** The list value standing for "no override" — the encoding is worked out from the bytes. */
const DETECT = 'detect';

/** Columns whose values are lists, and so the only ones a slash may usefully split. */
const LIST_FIELDS: readonly TripCsvField[] = ['caves', 'proposers', 'participants'];

interface Props {
  options: TripImportOptions;
  onChange: (next: TripImportOptions) => void;
  /** The sheet's own header line. Empty until the file has been read once. */
  header: readonly string[];
  /** What the reader settled the day/month order on, and how many rows ride on the choice. */
  dateOrderSource?: TripCsvDateOrderSource;
  /**
   * The order the file was actually read in, which is not always the one asked for: a date with
   * a component above twelve settles the question by itself, and then the stated choice is not
   * what any row was read under.
   */
  effectiveDateOrder?: TripCsvDateOrder;
  ambiguousDateRows: number;
  /** The character encoding the bytes were actually read under, and what settled that. */
  encoding?: TripCsvEncoding;
  encodingSource?: TripCsvEncodingSource;
  /** Header names the mapping claims that the sheet does not carry — a mapping about to yield nothing. */
  unmappedColumns: readonly string[];
}

/**
 * The choices a sheet import makes once for the whole file: which column is which, how a date
 * is read, what separates the values inside one cell, which missing records may be created,
 * and what every trip it writes is bound to.
 *
 * Two properties of this panel are load-bearing rather than cosmetic. The audience defaults to
 * the most restrictive value, because an import is a bulk action and a bulk action that
 * publishes by default publishes a club's whole history at once. And every create switch is off
 * until somebody asks for it, because creating a caver or a vocabulary word is a change to the
 * installation that outlives the import and is not undone by taking the import back.
 */
export default function TripImportOptionsPanel({
  options,
  onChange,
  header,
  dateOrderSource,
  effectiveDateOrder,
  ambiguousDateRows,
  encoding,
  encodingSource,
  unmappedColumns,
}: Props) {
  const { t } = useTranslation();
  const cavingGroups = useCavingGroups();

  // The file's own answer wins over the stated one, so the control has to show the order the
  // rows were read in rather than the order that was asked for. Showing the stated one put
  // "Day first" beside a tag reading "The file itself" on a sheet read month-first — two
  // statements contradicting each other about how a club's whole history was dated, next to a
  // control that appeared to do nothing when it was moved.
  const settledByFile = dateOrderSource === 'file';
  const shownDateOrder = settledByFile
    ? (effectiveDateOrder ?? options.dateOrder ?? 'dayFirst')
    : (options.dateOrder ?? 'dayFirst');

  // Same reasoning as the day/month order above: the control shows the encoding the bytes were
  // actually read under rather than an empty box, because a reviewer about to change it needs to
  // see what they are changing it from. It falls back to "work it out" only before the file has
  // been read at all, which is the one moment when there is no answer yet.
  const shownEncoding = options.encoding ?? encoding ?? DETECT;

  const set = <K extends keyof TripImportOptions>(key: K, value: TripImportOptions[K]) =>
    onChange({ ...options, [key]: value });

  const setColumn = (field: TripCsvField, value: string | undefined) => {
    const columns = { ...(options.columns ?? {}) };
    if (value === undefined || value === '') {
      delete columns[field];
    } else {
      columns[field] = value;
    }
    set('columns', columns);
  };

  const slashFields = options.slashSeparatedFields ?? [];
  const headerOptions = header.map((column) => ({ value: column, label: column }));

  return (
    <Card size="small" styles={{ body: { paddingBlock: 8 } }} data-testid="trip-import-options">
      <Collapse
        ghost
        defaultActiveKey={['reading']}
        items={[
          {
            key: 'reading',
            label: t('tripImport.optionsReading'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24}>
                  {/* Said before the control rather than after it. A sheet where nothing settles
                      the order reads perfectly either way, and the rows that would change are
                      the only warning anybody gets before a year of trips moves by six months. */}
                  <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                    {t('tripImport.dateOrderHint')}
                  </Typography.Paragraph>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('tripImport.dateOrder')} style={{ marginBottom: 8 }}>
                    <Select
                      value={shownDateOrder}
                      // Disabled where the file has already decided, because there the control
                      // cannot change the answer and a live control that changes nothing is a
                      // worse statement than an explained dead one.
                      disabled={settledByFile}
                      onChange={(value) => set('dateOrder', value)}
                      data-testid="trip-import-date-order"
                      options={[
                        { value: 'dayFirst', label: t('tripImport.dateOrderDayFirst') },
                        { value: 'monthFirst', label: t('tripImport.dateOrderMonthFirst') },
                      ]}
                    />
                    {settledByFile && (
                      <Typography.Text type="secondary" data-testid="trip-import-date-order-fixed">
                        {t('tripImport.dateOrderFixedByFile')}
                      </Typography.Text>
                    )}
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('tripImport.dateOrderSettled')} style={{ marginBottom: 8 }}>
                    <div data-testid="trip-import-date-order-source">
                      <Tag color={dateOrderSource === 'conflict' ? 'orange' : undefined}>
                        {dateOrderSource
                          ? t(`tripImport.dateOrderSources.${dateOrderSource}`)
                          : t('tripImport.dateOrderSources.stated')}
                      </Tag>
                      <Typography.Text type="secondary">
                        {t('tripImport.dateOrderRidesOn', { count: ambiguousDateRows })}
                      </Typography.Text>
                    </div>
                  </Form.Item>
                </Col>
                <Col xs={24}>
                  {/* Said before the control for the same reason the date order's hint is: a
                      sheet read under the wrong code page is not an error anybody sees, it is a
                      page of names with the diacritics quietly replaced. */}
                  <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                    {t('tripImport.encodingHint')}
                  </Typography.Paragraph>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('tripImport.encoding')} style={{ marginBottom: 8 }}>
                    <Select
                      value={shownEncoding}
                      onChange={(value) =>
                        set('encoding', value === DETECT ? undefined : (value as TripCsvEncoding))
                      }
                      data-testid="trip-import-encoding"
                      options={[
                        { value: DETECT, label: t('tripImport.encodingDetect') },
                        ...ENCODINGS.map((name) => ({
                          value: name,
                          label: t(`tripImport.encodings.${name}`),
                        })),
                      ]}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('tripImport.encodingSettled')} style={{ marginBottom: 8 }}>
                    <div data-testid="trip-import-encoding-source">
                      {/* A guess is the one answer worth colouring: it is the case where the
                          reading may be wrong and nothing in the text will say so. */}
                      <Tag color={encodingSource === 'guessed' ? 'orange' : undefined}>
                        {encodingSource
                          ? t(`tripImport.encodingSources.${encodingSource}`)
                          : t('tripImport.encodingSources.utf8')}
                      </Tag>
                      {encoding && (
                        <Typography.Text type="secondary">
                          {t(`tripImport.encodings.${encoding}`)}
                        </Typography.Text>
                      )}
                    </div>
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('tripImport.delimiter')}
                    tooltip={t('tripImport.delimiterHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Input
                      maxLength={1}
                      value={options.delimiter ?? ','}
                      onChange={(e) => set('delimiter', e.target.value)}
                      data-testid="trip-import-delimiter"
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    label={t('tripImport.separators')}
                    tooltip={t('tripImport.separatorsHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Input
                      maxLength={8}
                      value={options.multiValueSeparators ?? ';,'}
                      onChange={(e) => set('multiValueSeparators', e.target.value)}
                      data-testid="trip-import-separators"
                    />
                  </Form.Item>
                </Col>
                <Col xs={24}>
                  <Form.Item
                    label={t('tripImport.slashFields')}
                    tooltip={t('tripImport.slashFieldsHint')}
                    style={{ marginBottom: 8 }}
                  >
                    <Select
                      mode="multiple"
                      allowClear
                      value={slashFields.filter((field) => LIST_FIELDS.includes(field))}
                      onChange={(value: TripCsvField[]) => set('slashSeparatedFields', value)}
                      data-testid="trip-import-slash-fields"
                      options={LIST_FIELDS.map((field) => ({
                        value: field,
                        label: t(`tripImport.fields.${field}`),
                      }))}
                    />
                  </Form.Item>
                </Col>
              </Row>
            ),
          },
          {
            key: 'mapping',
            label: t('tripImport.optionsMapping'),
            children: (
              <Row gutter={[16, 8]}>
                {unmappedColumns.length > 0 && (
                  <Col xs={24}>
                    <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                      {t('tripImport.unmappedColumns', { columns: unmappedColumns.join(', ') })}
                    </Typography.Paragraph>
                  </Col>
                )}
                {FIELDS.map((field) => (
                  <Col xs={24} md={8} key={field}>
                    <Form.Item label={t(`tripImport.fields.${field}`)} style={{ marginBottom: 8 }}>
                      {/* The sheet's real headers, searchable: a club centralizator carries
                          twenty columns and typing one is faster than reading the list. When the
                          file has not been read yet there is nothing truthful to offer, so the
                          reviewer types the header name instead of picking a guess. */}
                      {headerOptions.length > 0 ? (
                        <Select
                          allowClear
                          showSearch
                          optionFilterProp="label"
                          placeholder={t('tripImport.mappingAuto')}
                          value={options.columns?.[field] ?? undefined}
                          onChange={(value?: string) => setColumn(field, value)}
                          data-testid={`trip-import-column-${field}`}
                          options={headerOptions}
                        />
                      ) : (
                        <Input
                          placeholder={t('tripImport.mappingAuto')}
                          value={options.columns?.[field] ?? ''}
                          onChange={(e) => setColumn(field, e.target.value)}
                          data-testid={`trip-import-column-${field}`}
                        />
                      )}
                    </Form.Item>
                  </Col>
                ))}
              </Row>
            ),
          },
          {
            key: 'created',
            label: t('tripImport.optionsCreated'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24}>
                  {/* Before the switches, because what they do is not symmetrical with the undo:
                      taking the import back removes the trips and the features it made, and
                      leaves behind every caver and every vocabulary word it added. */}
                  <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                    {t('tripImport.createHint')}
                  </Typography.Paragraph>
                </Col>
                <Col xs={24} md={12}>
                  <Checkbox
                    checked={options.createMissingCaves ?? false}
                    onChange={(e) => set('createMissingCaves', e.target.checked)}
                    data-testid="trip-import-create-caves"
                  >
                    {t('tripImport.createCaves')}
                  </Checkbox>
                </Col>
                <Col xs={24} md={12}>
                  <Checkbox
                    checked={options.createMissingAreas ?? false}
                    onChange={(e) => set('createMissingAreas', e.target.checked)}
                    data-testid="trip-import-create-areas"
                  >
                    {t('tripImport.createAreas')}
                  </Checkbox>
                </Col>
                <Col xs={24} md={12}>
                  <Checkbox
                    checked={options.createMissingCavers ?? false}
                    onChange={(e) =>
                      onChange({
                        ...options,
                        createMissingCavers: e.target.checked,

                        // Turning the roster switch off takes the widening one with it. Left
                        // standing it would widen nothing, while still moving the figures the
                        // reviewer reads: the short names stop counting as ones nobody can be
                        // made from, nothing takes their place in the count of people who would
                        // be created, and the standing warning goes out on a sheet where every
                        // one of those people is still dropped from the trip they went on.
                        createAbbreviatedCavers: e.target.checked
                          ? (options.createAbbreviatedCavers ?? false)
                          : false,
                      })
                    }
                    data-testid="trip-import-create-cavers"
                  >
                    {t('tripImport.createCavers')}
                  </Checkbox>
                  {/* Under the roster switch rather than beside it, and unusable without it,
                      because it widens what that switch creates rather than creating anything
                      itself. */}
                  <div style={{ marginTop: 8, marginInlineStart: 24 }}>
                    <Checkbox
                      checked={options.createAbbreviatedCavers ?? false}
                      disabled={!(options.createMissingCavers ?? false)}
                      onChange={(e) => set('createAbbreviatedCavers', e.target.checked)}
                      data-testid="trip-import-create-abbreviated-cavers"
                    >
                      {t('tripImport.createAbbreviatedCavers')}
                    </Checkbox>
                    <Typography.Paragraph
                      type="secondary"
                      style={{ marginTop: 4, marginBottom: 0 }}
                    >
                      {t('tripImport.createAbbreviatedCaversHint')}
                    </Typography.Paragraph>
                  </div>
                </Col>
                <Col xs={24} md={12}>
                  <Checkbox
                    checked={options.createMissingTripTypes ?? false}
                    onChange={(e) => set('createMissingTripTypes', e.target.checked)}
                    data-testid="trip-import-create-types"
                  >
                    {t('tripImport.createTripTypes')}
                  </Checkbox>
                </Col>
              </Row>
            ),
          },
          {
            key: 'audience',
            label: t('tripImport.optionsAudience'),
            children: (
              <Row gutter={[16, 8]}>
                <Col xs={24}>
                  <Typography.Paragraph type="secondary" style={{ marginBottom: 8 }}>
                    {t('tripImport.audienceHint')}
                  </Typography.Paragraph>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('tripImport.visibility')} style={{ marginBottom: 8 }}>
                    <Select
                      value={options.visibility ?? 'private'}
                      onChange={(value) => set('visibility', value)}
                      data-testid="trip-import-visibility"
                      options={(['private', 'cavingGroup', 'authenticated', 'public'] as const).map(
                        (value) => ({ value, label: t(`caves.visibilityValues.${value}`) }),
                      )}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item label={t('tripImport.cavingGroup')} style={{ marginBottom: 8 }}>
                    <Select
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      loading={cavingGroups.isLoading}
                      value={options.cavingGroupId ?? undefined}
                      onChange={(value?: string) => set('cavingGroupId', value ?? null)}
                      data-testid="trip-import-caving-group"
                      options={(cavingGroups.data ?? []).map((group) => ({
                        value: group.id,
                        label: group.name,
                      }))}
                    />
                  </Form.Item>
                </Col>
              </Row>
            ),
          },
        ]}
      />
    </Card>
  );
}
