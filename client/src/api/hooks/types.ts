// SPDX-License-Identifier: AGPL-3.0-or-later
import type { components } from '../schema';

export type CaveListItem = components['schemas']['CaveListItemDto'];
export type CaveDetail = components['schemas']['CaveDto'];
export type CaveWrite = components['schemas']['CaveWriteRequest'];
export type Entrance = components['schemas']['EntranceDto'];
export type EntranceWrite = components['schemas']['EntranceWriteRequest'];
export type MapLayerInfo = components['schemas']['MapLayerDto'];
export type Taxonomy = components['schemas']['TaxonomyDto'];
export type FeatureType = components['schemas']['FeatureTypeDto'];
export type DocumentType = components['schemas']['DocumentTypeDto'];
export type DocumentInfo = components['schemas']['DocumentDto'];
export type DocumentComment = components['schemas']['DocumentCommentDto'];
export type TextExtractionState = components['schemas']['TextExtractionState'];
export type CabinetInfo = components['schemas']['CabinetDto'];
export type CabinetWrite = components['schemas']['CabinetWriteRequest'];
export type CabinetDocument = components['schemas']['CabinetDocumentDto'];
export type UploadBatchInfo = components['schemas']['UploadBatchDto'];
export type UploadBatchItemInfo = components['schemas']['UploadBatchItemDto'];
export type UnfiledDocument = components['schemas']['UnfiledDocumentDto'];
export type PhotoInfo = components['schemas']['PhotoDto'];
export type PhotoCredit = components['schemas']['PhotoCreditDto'];
export type AlbumInfo = components['schemas']['AlbumDto'];
export type PublicPhoto = components['schemas']['PublicPhotoDto'];
export type DeletedPhoto = components['schemas']['DeletedPhotoDto'];
export type Visibility = components['schemas']['Visibility'];
export type FileConfig = components['schemas']['FileConfigDto'];
export type EntranceFeatureCollection = components['schemas']['FeatureCollection'];
export type Me = components['schemas']['MeDto'];
export type MeUpdate = components['schemas']['MeUpdateRequest'];
export type MeLocale = components['schemas']['MeLocaleDto'];
export type MeLocaleWrite = components['schemas']['MeLocaleWriteRequest'];
export type ProfileVisibility = components['schemas']['ProfileVisibilityDto'];
export type FieldVisibility = ProfileVisibility['email'];
export type UserAddress = components['schemas']['UserAddressDto'];
export type UserAddressWrite = components['schemas']['UserAddressWriteRequest'];
export type MemberSummary = components['schemas']['MemberDto'];
export type NotificationPreferences = components['schemas']['NotificationPreferencesDto'];
export type NotificationCategory = components['schemas']['NotificationCategoryDto'];
export type NotificationChannelCell = components['schemas']['NotificationChannelDto'];
export type NotificationChoice = components['schemas']['NotificationChannelChoice'];

/**
 * One channel of the preference matrix, as the wire spells it. The server's channel type is a
 * set of bit flags, so it crosses the boundary as a string rather than as a closed union, and a
 * cell always names exactly one of these. Written out here because the wording lookup, the
 * column order and the exhaustiveness check all need a closed list, and the generated client
 * cannot give them one.
 */
export type NotificationChannelName = 'inApp' | 'email' | 'sms';

/**
 * The name of one notification category, as the server publishes it. Named separately from the
 * row that carries it because the settings page, the opt-out landing page and the inbox all look
 * their wording up by this value alone. Non-null by construction: the generated union admits null only
 * because one response omits the category — a daily summary collects every category and names
 * none — and null is not a category anybody can be notified about.
 */
export type NotificationCategoryName = NonNullable<components['schemas']['NotificationCategory']>;

/**
 * One line of the reader's own inbox, as the server renders it.
 *
 * The wording is rendered on the server from the language the request was made in, so `title` is
 * text to show rather than a key to look up. `targetWithheld` says the reader may no longer see
 * the thing this row is about: the row is still listed — that it happened is not the secret — but
 * it carries neither the name nor the link, and has to be shown as deliberate rather than broken.
 */
export type NotificationItem = components['schemas']['NotificationDto'];

export type NotificationHealth = components['schemas']['NotificationHealthDto'];
export type NotificationDeliveryRow = components['schemas']['NotificationDeliveryDto'];
export type NotificationDeliveryStatus = components['schemas']['NotificationDeliveryStatus'];
export type NotificationDeliveryChannel = components['schemas']['NotificationChannel'];
export type NotificationRetryResult = components['schemas']['NotificationRetryDto'];
export type NotificationRetryOutcome = components['schemas']['NotificationRetryOutcome'];

/** How many lines of the reader's own inbox are still unopened. */
export type UnreadNotificationCount = components['schemas']['UnreadNotificationCountDto'];

/** What an opt-out link switched off, as the server reports it back to the landing page. */
export type UnsubscribeResult = components['schemas']['UnsubscribeResultDto'];
export type DataExport = components['schemas']['DataExportDto'];
/** One of the caller's calendar feed addresses — metadata only; the address itself is never listed. */
export type CalendarFeed = components['schemas']['CalendarFeedDto'];
export type CalendarFeedList = components['schemas']['CalendarFeedListDto'];
/** The mint answer: the one time the subscription address is ever shown. */
export type CalendarFeedCreated = components['schemas']['CalendarFeedCreatedDto'];
export type MfaStatus = components['schemas']['MfaStatusDto'];
export type MfaMethod = components['schemas']['MfaMethodDto'];
export type TwoFactorMethod = components['schemas']['TwoFactorMethod'];
export type PhoneStatus = components['schemas']['PhoneStatusDto'];
export type AdminSettings = components['schemas']['AdminSettingsDto'];
export type MailSettingsWrite = components['schemas']['MailSettingsWriteRequest'];
export type SmsSettingsWrite = components['schemas']['SmsSettingsWriteRequest'];
export type SecuritySettings = components['schemas']['SecuritySettingsDto'];
export type ProtectionSettings = components['schemas']['ProtectionSettingsDto'];
export type ImportSettings = components['schemas']['ImportSettingsDto'];
export type NotificationSettings = components['schemas']['NotificationSettingsDto'];
export type AnnouncementSettings = components['schemas']['AnnouncementSettingsDto'];
/**
 * Which neighbouring photo libraries this installation has stopped using.
 *
 * Only ever stops one it already has. Whether the installation has a photo library at all comes
 * from the deployment that gave it an address and a credential, so there is no value here that
 * connects one — a screen offering that would be claiming a library nobody supplied.
 */
export type PhotoLibrarySuspension = components['schemas']['PhotoLibrarySuspensionDto'];
export type MessageTemplate = components['schemas']['MessageTemplateDto'];
export type ResLink = components['schemas']['ResLinkDto'];
export type ResLinkMember = components['schemas']['ResLinkMemberDto'];
export type ResLinkTargetDisplay = components['schemas']['ResLinkTargetDisplayDto'];
export type ResLinkTargetHit = components['schemas']['ResLinkTargetHitDto'];
export type ResLinkRelationType = components['schemas']['ResLinkRelationTypeDto'];
export type ResLinkRelationTypeWrite = components['schemas']['ResLinkRelationTypeWriteRequest'];
export type ResLinkCreate = components['schemas']['ResLinkCreateRequest'];
export type ResLinkUpdate = components['schemas']['ResLinkUpdateRequest'];
export type ResLinkMemberAdd = components['schemas']['ResLinkMemberAddRequest'];
export type ResLinkMemberUpdate = components['schemas']['ResLinkMemberUpdateRequest'];
export type ResLinkPointDefault = components['schemas']['ResLinkPointDefaultDto'];
export type AnchorKind = components['schemas']['AnchorKind'];
export type ResLinkAnchorState = components['schemas']['ResLinkAnchorState'];
export type AnnotatedText = components['schemas']['AnnotatedTextDto'];
export type AnnotatedTextWrite = components['schemas']['AnnotatedTextWriteDto'];
export type AnnotatedTextCreate = components['schemas']['AnnotatedTextCreateRequest'];
export type ReanchorReport = components['schemas']['ReanchorReportDto'];

export interface NotificationListParams {
  category?: NotificationCategoryName;
  unreadOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface MemberListParams {
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface CaveListParams {
  page?: number;
  pageSize?: number;
  sort?: string;
  caveTypeId?: number;
  region?: string;
  search?: string;
  minLength?: number;
  bbox?: string;
  tag?: string;
}
export type AuditEntry = components['schemas']['AuditEntryDto'];

/**
 * What a page of a library is asked for. No rectangle, by construction — and no words either:
 * asking a library what it holds and asking what it makes of a sentence are different questions
 * with differently shaped answers, and they are different routes.
 */
export interface LibraryPhotographQuery {
  page: number;
  pageSize: number;
  /**
   * A trip whose days the listing is narrowed to, or nothing for the whole library.
   *
   * A trip and never a pair of dates, and that is the feature rather than a precaution: the server
   * reads the trip's own start and end, so a panel saying "taken while this trip was out" is saying
   * something that was checked. A window a browser chose would be a date filter with a trip's name
   * written over it.
   */
  tripId?: string;
  /**
   * An album of the library the listing is narrowed to, or nothing for the whole library.
   *
   * The identifier the library itself gave for one of its own albums, taken from the chooser rather
   * than composed here. The server checks its shape before it goes anywhere near the library,
   * because one of the two products reads a value like this as a term in its own search grammar.
   */
  albumId?: string;
}

/** What a search of a library is asked for. */
export interface LibrarySearchQuery {
  /** The words. Never empty: the server refuses a search with nothing to search for. */
  q: string;
  page: number;
  pageSize: number;
}

export type FeatureKind = components['schemas']['FeatureKind'];
export type FeatureCategory = components['schemas']['FeatureCategory'];

export interface FeatureListParams {
  page?: number;
  pageSize?: number;
  kind?: FeatureKind;
  featureTypeId?: number;
  category?: FeatureCategory;
  bbox?: string;
  tag?: string;
  search?: string;
  /** A named set of objects — what a multi-selection asks about. Bounded by the server. */
  ids?: string[];
}

export interface FeatureChildrenParams {
  page?: number;
  pageSize?: number;
}

export interface GeofileListParams {
  page?: number;
  pageSize?: number;
  search?: string;
}
export type AttachmentInfo = components['schemas']['AttachmentDto'];

/**
 * entityType vocabulary shared by attachments, taggings and ACLs. Any feature — generic,
 * cave, entrance or centerline — is addressed as 'feature' with its feature id. The wire
 * type is a plain string; this union is the documented set of accepted values.
 */
export type EntityType =
  | 'feature'
  | 'tripLog'
  | 'geofile'
  | 'georeferencedMap'
  | 'mapView'
  | 'expedition'
  | 'event'
  | 'checklist';
// Stored files additionally carry taggings (never attachments or grants) — the tag
// endpoints accept the extra target; the server rejects it everywhere else.
export type AttachedEntityType = EntityType | 'storedFile';
export type AttachmentRole = AttachmentInfo['role'];

export interface CabinetDocumentParams {
  includeSubtree?: boolean;
  page?: number;
  pageSize?: number;
}

export interface RasterMapListParams {
  page?: number;
  pageSize?: number;
  caveFeatureId?: string;
}
export type ActivityState = components['schemas']['ActivityState'];

export interface TripLogListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  caveId?: string;
  search?: string;
  /**
   * The camp's own trip list. It is filtered like every other listing, so the same camp lists
   * different trips to different people; a camp the caller may not read answers as though it
   * gathered nothing rather than refusing, so an id cannot be probed for existence here.
   */
  expeditionId?: string;
  /**
   * Area features the trips name, comma-separated. Alternatives: a trip naming any of them is
   * kept, and each one reaches everything the containment hierarchy puts inside it. Like the cave
   * and the camp, an area this caller may not read answers with an empty page rather than a
   * refusal, so an id cannot be probed for existence — and one such id empties the whole answer
   * rather than being dropped from the list.
   */
  areaIds?: string;
  /**
   * People on the roster, comma-separated and alternatives to each other. The answer is still only
   * the trips this caller may read, so it never assembles where a person has been out of trips the
   * asker cannot open — which is why the caller's own list of trips, whose whole point is that it
   * names nobody, has no such member.
   */
  participantIds?: string;
  /** Trip type ids, comma-separated. Alternatives: a trip of any of them is kept. */
  types?: string;
  /** Lifecycle words, comma-separated, spelled the way the contract spells them. */
  states?: string;
  /** Audience words, comma-separated, spelled the way the contract spells them. */
  visibilities?: string;
  /** Whether something went wrong. Omitted means no opinion, not "no". */
  hadIncident?: boolean;
  /**
   * `date`, `title`, `created` or `updated`, a leading minus for descending. The order is the
   * server's: re-sorting the page it handed back would reorder one page, which is a different
   * and wrong answer as soon as there is more than one.
   */
  sort?: string;
}

/**
 * Everything the counts are asked about. It is the listing's own parameters minus the three that
 * change no count — which page, how big, and in what order — so a panel and the page it sits over
 * are demonstrably asking one question.
 */
export type TripLogFacetParams = Omit<TripLogListParams, 'page' | 'pageSize' | 'sort'>;

/**
 * What the slices are cut from and what they are cut by. The narrowings are the listing's own,
 * minus the three that change no slice — which page, how big, and in what order — so the shape
 * above the table and the table itself are demonstrably about one set of trips.
 */
export type TripLogGroupingParams = TripLogFacetParams & {
  /** `year`, `type`, `state`, `visibility`, `incident`, `area` or `participant`. */
  groupBy?: string;
  /** The second level. Must differ from the first, which the server refuses rather than ignores. */
  thenBy?: string;
};

/**
 * What the caller asked of their own list of trips. There is deliberately no member naming a
 * person: whose trips these are is worked out on the server from whoever is making the request,
 * and a parameter for it would let somebody assemble where a named person has been out of trips
 * they may never open. Adding one here would be the first half of undoing that.
 */
export interface MyTripLogListParams {
  page?: number;
  pageSize?: number;
  /** Inclusive, `YYYY-MM-DD`. Omitted, the server starts the window at today. */
  from?: string;
  /** Inclusive, `YYYY-MM-DD`. Omitted, the window has no far end. */
  to?: string;
  /** A lifecycle state spelled the way the contract spells it; an unknown word is refused. */
  state?: ActivityState;
}

// ---------------------------------------------------------------------------
// Resource links
// ---------------------------------------------------------------------------

export interface ResLinkPageParams {
  page?: number;
  pageSize?: number;
  /**
   * A relation code, narrowing the panel to links of that one relation — how a page asks
   * for a single role rather than reading every link and sorting them itself. Omitted or
   * blank means every relation, untyped links included; a code no relation type carries is
   * refused by the server rather than answered with an empty page.
   */
  relation?: string;
}

export interface ImportBatchListParams {
  geofileId?: string;
  page?: number;
  pageSize?: number;
}

export type SpeologieSearchParams = {
  q?: string;
  county?: string;
  basin?: number;
  page?: number;
  pageSize?: number;
};

export interface UnfiledDocumentParams {
  uploadBatchId?: string;
  page?: number;
  pageSize?: number;
}

export interface UploadBatchItemParams {
  outcome?: 'pending' | 'stored' | 'skipped' | 'failed';
  page?: number;
  pageSize?: number;
}

/** What the gallery can be narrowed by. Every field is optional and they combine. */
export interface PhotoQueryParams {
  caveId?: string;
  featureId?: string;
  tripLogId?: string;
  /**
   * One camp's pictures: those filed against the camp and those on the trips it gathers that the
   * caller may read. A camp the caller may not read answers empty rather than refusing, so the
   * id cannot be used to find out which trips a camp holds.
   */
  expeditionId?: string;
  caverId?: string;
  tagId?: number;
  albumId?: string;
  uploadBatchId?: string;
  camera?: string;
  /** The map extent, as west,south,east,north. */
  bbox?: string;
  unplaced?: boolean;
  from?: string;
  to?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface AlbumListParams {
  subjectEntityId?: string;
  page?: number;
  pageSize?: number;
}

// ---------------------------------------------------------------------------
// The one filter route, and the two that describe it
// ---------------------------------------------------------------------------

export type FilterQueryBody = components['schemas']['FilterQueryRequest'];

export interface TerrainBuildPageParams {
  page: number;
  pageSize: number;
}

/**
 * How the camp list is narrowed. The window asks what a camp overlapped rather than what it
 * started inside, so a fortnight camp running across the end of a month is in both months; the
 * word is looked for in the name; the state is one of the camp lifecycle's own, and a word the
 * server does not have is refused rather than ignored.
 */
export interface ExpeditionListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  search?: string;
  state?: string;
}

export interface TrackingEventListParams {
  caverId?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}
export type PublishedLinkStatus = components['schemas']['PublishedLinkStatus'];

/** What the list of published links may be ordered by, spelled as the server reads it. */
export type PublishedLinkSort =
  | 'createdAt'
  | 'expiresAt'
  | 'tripDate'
  | 'tripTitle'
  | 'status'
  | 'watchArmedAt';

export interface PublishedLinksParams {
  status?: PublishedLinkStatus;
  /**
   * Keeps only links whose tracking is running and was started more than this many days ago.
   * Sent with each question and stored nowhere: how long is too long is the reader's call. The
   * server refuses a negative number and one over ten years.
   */
  armedLongerThanDays?: number;
  sort?: PublishedLinkSort;
  descending?: boolean;
  page: number;
  pageSize: number;
}

/**
 * What the caller asked of the calendar.
 *
 * The window is required, both ends of it, and that is the whole reason this answer can be one
 * merged list rather than an approximation: within a bounded window each source's readable rows
 * are a finite set, so merging them is exact. An unbounded question would have to read each
 * source ahead and hope.
 *
 * There is deliberately no member naming a person. `mine` means whoever is making the request and
 * is worked out on the server from the request itself; a parameter carrying somebody's identifier
 * would let a reader assemble where a named person has been out of rows they may never open.
 * `cavingGroupId` names a group and not a person, and it can only ever narrow what the reader
 * could already read.
 */
export interface CalendarParams {
  /** Inclusive, `YYYY-MM-DD`. Required. */
  from: string;
  /** Inclusive, `YYYY-MM-DD`. Required. */
  to: string;
  /**
   * The families of dated record wanted, comma-separated. Omitted means all of them, which is
   * the point of the surface; naming several is how "everything except one family" is asked for,
   * which a single word cannot express once there are more than two families.
   */
  source?: string;
  /**
   * The kinds of event wanted, comma-separated. It narrows the events and nothing else: a trip
   * and a camp have no kind, so they stay exactly where `source` put them rather than being
   * dropped by a question they cannot answer. Omitted means every kind. A word the server does
   * not have is refused rather than ignored.
   */
  kind?: string;
  /** A lifecycle state spelled the way the contract spells it. */
  state?: ActivityState;
  /** One group's calendar: the trips it is running and the camps it owns. */
  cavingGroupId?: string;
  /** The rows the signed-in account is on. Takes no argument, and never will. */
  mine?: boolean;
  /** False narrows the window to begin no earlier than today, in the server's clock. */
  includePast?: boolean;
  /** False leaves out the rows that were called off. They are in by default. */
  includeCancelled?: boolean;
  /** One of the orders the server knows; anything else falls back to the calendar's own. */
  sort?: string;
}

/**
 * How the event list is narrowed. The window asks what an event *overlapped* rather than what it
 * started inside, so a training weekend running across the end of a month is in both months; the
 * word is looked for in the title; and a kind or a state the server does not have is refused
 * rather than quietly answered with an empty page.
 */
export interface EventListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  search?: string;
  kind?: string;
  state?: string;
  /**
   * One repeating event's occurrences, by the key they share. A run is not a thing of its own —
   * it is the ordinary events carrying this key — so it is asked for as a narrowing of the list
   * and answered through the same visibility walk as every other narrowing.
   */
  seriesId?: string;
}
