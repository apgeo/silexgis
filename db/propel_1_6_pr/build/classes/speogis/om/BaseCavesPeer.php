<?php


/**
 * Base static class for performing query and update operations on the 'caves' table.
 *
 *
 *
 * @package propel.generator.speogis.om
 */
abstract class BaseCavesPeer
{

    /** the default database name for this class */
    const DATABASE_NAME = 'speogis';

    /** the table name for this class */
    const TABLE_NAME = 'caves';

    /** the related Propel class for this table */
    const OM_CLASS = 'Caves';

    /** the related TableMap class for this table */
    const TM_CLASS = 'CavesTableMap';

    /** The total number of columns. */
    const NUM_COLUMNS = 32;

    /** The number of lazy-loaded columns. */
    const NUM_LAZY_LOAD_COLUMNS = 0;

    /** The number of columns to hydrate (NUM_COLUMNS - NUM_LAZY_LOAD_COLUMNS) */
    const NUM_HYDRATE_COLUMNS = 32;

    /** the column name for the id field */
    const ID = 'caves.id';

    /** the column name for the name field */
    const NAME = 'caves.name';

    /** the column name for the type_id field */
    const TYPE_ID = 'caves.type_id';

    /** the column name for the identification_code field */
    const IDENTIFICATION_CODE = 'caves.identification_code';

    /** the column name for the description field */
    const DESCRIPTION = 'caves.description';

    /** the column name for the user_id field */
    const USER_ID = 'caves.user_id';

    /** the column name for the other_toponyms field */
    const OTHER_TOPONYMS = 'caves.other_toponyms';

    /** the column name for the rock_type_id field */
    const ROCK_TYPE_ID = 'caves.rock_type_id';

    /** the column name for the rock_age field */
    const ROCK_AGE = 'caves.rock_age';

    /** the column name for the hydrographic_basin field */
    const HYDROGRAPHIC_BASIN = 'caves.hydrographic_basin';

    /** the column name for the valley field */
    const VALLEY = 'caves.valley';

    /** the column name for the tributary_river field */
    const TRIBUTARY_RIVER = 'caves.tributary_river';

    /** the column name for the closest_address field */
    const CLOSEST_ADDRESS = 'caves.closest_address';

    /** the column name for the is_show_cave field */
    const IS_SHOW_CAVE = 'caves.is_show_cave';

    /** the column name for the show_cave_length field */
    const SHOW_CAVE_LENGTH = 'caves.show_cave_length';

    /** the column name for the website field */
    const WEBSITE = 'caves.website';

    /** the column name for the land_registry_number field */
    const LAND_REGISTRY_NUMBER = 'caves.land_registry_number';

    /** the column name for the region field */
    const REGION = 'caves.region';

    /** the column name for the depth field */
    const DEPTH = 'caves.depth';

    /** the column name for the surveyed_length field */
    const SURVEYED_LENGTH = 'caves.surveyed_length';

    /** the column name for the discovery_date field */
    const DISCOVERY_DATE = 'caves.discovery_date';

    /** the column name for the discoverer field */
    const DISCOVERER = 'caves.discoverer';

    /** the column name for the volume field */
    const VOLUME = 'caves.volume';

    /** the column name for the positive_depth field */
    const POSITIVE_DEPTH = 'caves.positive_depth';

    /** the column name for the negative_depth field */
    const NEGATIVE_DEPTH = 'caves.negative_depth';

    /** the column name for the ramification_index field */
    const RAMIFICATION_INDEX = 'caves.ramification_index';

    /** the column name for the real_extension field */
    const REAL_EXTENSION = 'caves.real_extension';

    /** the column name for the cave_age field */
    const CAVE_AGE = 'caves.cave_age';

    /** the column name for the projected_extension field */
    const PROJECTED_EXTENSION = 'caves.projected_extension';

    /** the column name for the exploration_status field */
    const EXPLORATION_STATUS = 'caves.exploration_status';

    /** the column name for the protection_class field */
    const PROTECTION_CLASS = 'caves.protection_class';

    /** the column name for the potential_depth field */
    const POTENTIAL_DEPTH = 'caves.potential_depth';

    /** The enumerated values for the exploration_status field */
    const EXPLORATION_STATUS_UNKNOWN = 'Unknown';
    const EXPLORATION_STATUS_NOT_EXPLORED = 'Not explored';
    const EXPLORATION_STATUS_PARTIALLY_EXPLORED = 'Partially explored';
    const EXPLORATION_STATUS_EXPLORATION_FINISHED = 'Exploration finished';

    /** The default string format for model objects of the related table **/
    const DEFAULT_STRING_FORMAT = 'YAML';

    /**
     * An identity map to hold any loaded instances of Caves objects.
     * This must be public so that other peer classes can access this when hydrating from JOIN
     * queries.
     * @var        array Caves[]
     */
    public static $instances = array();


    /**
     * holds an array of fieldnames
     *
     * first dimension keys are the type constants
     * e.g. CavesPeer::$fieldNames[CavesPeer::TYPE_PHPNAME][0] = 'Id'
     */
    protected static $fieldNames = array (
        BasePeer::TYPE_PHPNAME => array ('Id', 'Name', 'TypeId', 'IdentificationCode', 'Description', 'UserId', 'OtherToponyms', 'RockTypeId', 'RockAge', 'HydrographicBasin', 'Valley', 'TributaryRiver', 'ClosestAddress', 'IsShowCave', 'ShowCaveLength', 'Website', 'LandRegistryNumber', 'Region', 'Depth', 'SurveyedLength', 'DiscoveryDate', 'Discoverer', 'Volume', 'PositiveDepth', 'NegativeDepth', 'RamificationIndex', 'RealExtension', 'CaveAge', 'ProjectedExtension', 'ExplorationStatus', 'ProtectionClass', 'PotentialDepth', ),
        BasePeer::TYPE_STUDLYPHPNAME => array ('id', 'name', 'typeId', 'identificationCode', 'description', 'userId', 'otherToponyms', 'rockTypeId', 'rockAge', 'hydrographicBasin', 'valley', 'tributaryRiver', 'closestAddress', 'isShowCave', 'showCaveLength', 'website', 'landRegistryNumber', 'region', 'depth', 'surveyedLength', 'discoveryDate', 'discoverer', 'volume', 'positiveDepth', 'negativeDepth', 'ramificationIndex', 'realExtension', 'caveAge', 'projectedExtension', 'explorationStatus', 'protectionClass', 'potentialDepth', ),
        BasePeer::TYPE_COLNAME => array (CavesPeer::ID, CavesPeer::NAME, CavesPeer::TYPE_ID, CavesPeer::IDENTIFICATION_CODE, CavesPeer::DESCRIPTION, CavesPeer::USER_ID, CavesPeer::OTHER_TOPONYMS, CavesPeer::ROCK_TYPE_ID, CavesPeer::ROCK_AGE, CavesPeer::HYDROGRAPHIC_BASIN, CavesPeer::VALLEY, CavesPeer::TRIBUTARY_RIVER, CavesPeer::CLOSEST_ADDRESS, CavesPeer::IS_SHOW_CAVE, CavesPeer::SHOW_CAVE_LENGTH, CavesPeer::WEBSITE, CavesPeer::LAND_REGISTRY_NUMBER, CavesPeer::REGION, CavesPeer::DEPTH, CavesPeer::SURVEYED_LENGTH, CavesPeer::DISCOVERY_DATE, CavesPeer::DISCOVERER, CavesPeer::VOLUME, CavesPeer::POSITIVE_DEPTH, CavesPeer::NEGATIVE_DEPTH, CavesPeer::RAMIFICATION_INDEX, CavesPeer::REAL_EXTENSION, CavesPeer::CAVE_AGE, CavesPeer::PROJECTED_EXTENSION, CavesPeer::EXPLORATION_STATUS, CavesPeer::PROTECTION_CLASS, CavesPeer::POTENTIAL_DEPTH, ),
        BasePeer::TYPE_RAW_COLNAME => array ('ID', 'NAME', 'TYPE_ID', 'IDENTIFICATION_CODE', 'DESCRIPTION', 'USER_ID', 'OTHER_TOPONYMS', 'ROCK_TYPE_ID', 'ROCK_AGE', 'HYDROGRAPHIC_BASIN', 'VALLEY', 'TRIBUTARY_RIVER', 'CLOSEST_ADDRESS', 'IS_SHOW_CAVE', 'SHOW_CAVE_LENGTH', 'WEBSITE', 'LAND_REGISTRY_NUMBER', 'REGION', 'DEPTH', 'SURVEYED_LENGTH', 'DISCOVERY_DATE', 'DISCOVERER', 'VOLUME', 'POSITIVE_DEPTH', 'NEGATIVE_DEPTH', 'RAMIFICATION_INDEX', 'REAL_EXTENSION', 'CAVE_AGE', 'PROJECTED_EXTENSION', 'EXPLORATION_STATUS', 'PROTECTION_CLASS', 'POTENTIAL_DEPTH', ),
        BasePeer::TYPE_FIELDNAME => array ('id', 'name', 'type_id', 'identification_code', 'description', 'user_id', 'other_toponyms', 'rock_type_id', 'rock_age', 'hydrographic_basin', 'valley', 'tributary_river', 'closest_address', 'is_show_cave', 'show_cave_length', 'website', 'land_registry_number', 'region', 'depth', 'surveyed_length', 'discovery_date', 'discoverer', 'volume', 'positive_depth', 'negative_depth', 'ramification_index', 'real_extension', 'cave_age', 'projected_extension', 'exploration_status', 'protection_class', 'potential_depth', ),
        BasePeer::TYPE_NUM => array (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, )
    );

    /**
     * holds an array of keys for quick access to the fieldnames array
     *
     * first dimension keys are the type constants
     * e.g. CavesPeer::$fieldNames[BasePeer::TYPE_PHPNAME]['Id'] = 0
     */
    protected static $fieldKeys = array (
        BasePeer::TYPE_PHPNAME => array ('Id' => 0, 'Name' => 1, 'TypeId' => 2, 'IdentificationCode' => 3, 'Description' => 4, 'UserId' => 5, 'OtherToponyms' => 6, 'RockTypeId' => 7, 'RockAge' => 8, 'HydrographicBasin' => 9, 'Valley' => 10, 'TributaryRiver' => 11, 'ClosestAddress' => 12, 'IsShowCave' => 13, 'ShowCaveLength' => 14, 'Website' => 15, 'LandRegistryNumber' => 16, 'Region' => 17, 'Depth' => 18, 'SurveyedLength' => 19, 'DiscoveryDate' => 20, 'Discoverer' => 21, 'Volume' => 22, 'PositiveDepth' => 23, 'NegativeDepth' => 24, 'RamificationIndex' => 25, 'RealExtension' => 26, 'CaveAge' => 27, 'ProjectedExtension' => 28, 'ExplorationStatus' => 29, 'ProtectionClass' => 30, 'PotentialDepth' => 31, ),
        BasePeer::TYPE_STUDLYPHPNAME => array ('id' => 0, 'name' => 1, 'typeId' => 2, 'identificationCode' => 3, 'description' => 4, 'userId' => 5, 'otherToponyms' => 6, 'rockTypeId' => 7, 'rockAge' => 8, 'hydrographicBasin' => 9, 'valley' => 10, 'tributaryRiver' => 11, 'closestAddress' => 12, 'isShowCave' => 13, 'showCaveLength' => 14, 'website' => 15, 'landRegistryNumber' => 16, 'region' => 17, 'depth' => 18, 'surveyedLength' => 19, 'discoveryDate' => 20, 'discoverer' => 21, 'volume' => 22, 'positiveDepth' => 23, 'negativeDepth' => 24, 'ramificationIndex' => 25, 'realExtension' => 26, 'caveAge' => 27, 'projectedExtension' => 28, 'explorationStatus' => 29, 'protectionClass' => 30, 'potentialDepth' => 31, ),
        BasePeer::TYPE_COLNAME => array (CavesPeer::ID => 0, CavesPeer::NAME => 1, CavesPeer::TYPE_ID => 2, CavesPeer::IDENTIFICATION_CODE => 3, CavesPeer::DESCRIPTION => 4, CavesPeer::USER_ID => 5, CavesPeer::OTHER_TOPONYMS => 6, CavesPeer::ROCK_TYPE_ID => 7, CavesPeer::ROCK_AGE => 8, CavesPeer::HYDROGRAPHIC_BASIN => 9, CavesPeer::VALLEY => 10, CavesPeer::TRIBUTARY_RIVER => 11, CavesPeer::CLOSEST_ADDRESS => 12, CavesPeer::IS_SHOW_CAVE => 13, CavesPeer::SHOW_CAVE_LENGTH => 14, CavesPeer::WEBSITE => 15, CavesPeer::LAND_REGISTRY_NUMBER => 16, CavesPeer::REGION => 17, CavesPeer::DEPTH => 18, CavesPeer::SURVEYED_LENGTH => 19, CavesPeer::DISCOVERY_DATE => 20, CavesPeer::DISCOVERER => 21, CavesPeer::VOLUME => 22, CavesPeer::POSITIVE_DEPTH => 23, CavesPeer::NEGATIVE_DEPTH => 24, CavesPeer::RAMIFICATION_INDEX => 25, CavesPeer::REAL_EXTENSION => 26, CavesPeer::CAVE_AGE => 27, CavesPeer::PROJECTED_EXTENSION => 28, CavesPeer::EXPLORATION_STATUS => 29, CavesPeer::PROTECTION_CLASS => 30, CavesPeer::POTENTIAL_DEPTH => 31, ),
        BasePeer::TYPE_RAW_COLNAME => array ('ID' => 0, 'NAME' => 1, 'TYPE_ID' => 2, 'IDENTIFICATION_CODE' => 3, 'DESCRIPTION' => 4, 'USER_ID' => 5, 'OTHER_TOPONYMS' => 6, 'ROCK_TYPE_ID' => 7, 'ROCK_AGE' => 8, 'HYDROGRAPHIC_BASIN' => 9, 'VALLEY' => 10, 'TRIBUTARY_RIVER' => 11, 'CLOSEST_ADDRESS' => 12, 'IS_SHOW_CAVE' => 13, 'SHOW_CAVE_LENGTH' => 14, 'WEBSITE' => 15, 'LAND_REGISTRY_NUMBER' => 16, 'REGION' => 17, 'DEPTH' => 18, 'SURVEYED_LENGTH' => 19, 'DISCOVERY_DATE' => 20, 'DISCOVERER' => 21, 'VOLUME' => 22, 'POSITIVE_DEPTH' => 23, 'NEGATIVE_DEPTH' => 24, 'RAMIFICATION_INDEX' => 25, 'REAL_EXTENSION' => 26, 'CAVE_AGE' => 27, 'PROJECTED_EXTENSION' => 28, 'EXPLORATION_STATUS' => 29, 'PROTECTION_CLASS' => 30, 'POTENTIAL_DEPTH' => 31, ),
        BasePeer::TYPE_FIELDNAME => array ('id' => 0, 'name' => 1, 'type_id' => 2, 'identification_code' => 3, 'description' => 4, 'user_id' => 5, 'other_toponyms' => 6, 'rock_type_id' => 7, 'rock_age' => 8, 'hydrographic_basin' => 9, 'valley' => 10, 'tributary_river' => 11, 'closest_address' => 12, 'is_show_cave' => 13, 'show_cave_length' => 14, 'website' => 15, 'land_registry_number' => 16, 'region' => 17, 'depth' => 18, 'surveyed_length' => 19, 'discovery_date' => 20, 'discoverer' => 21, 'volume' => 22, 'positive_depth' => 23, 'negative_depth' => 24, 'ramification_index' => 25, 'real_extension' => 26, 'cave_age' => 27, 'projected_extension' => 28, 'exploration_status' => 29, 'protection_class' => 30, 'potential_depth' => 31, ),
        BasePeer::TYPE_NUM => array (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, )
    );

    /** The enumerated values for this table */
    protected static $enumValueSets = array(
        CavesPeer::EXPLORATION_STATUS => array(
            CavesPeer::EXPLORATION_STATUS_UNKNOWN,
            CavesPeer::EXPLORATION_STATUS_NOT_EXPLORED,
            CavesPeer::EXPLORATION_STATUS_PARTIALLY_EXPLORED,
            CavesPeer::EXPLORATION_STATUS_EXPLORATION_FINISHED,
        ),
    );

    /**
     * Translates a fieldname to another type
     *
     * @param      string $name field name
     * @param      string $fromType One of the class type constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME
     *                         BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM
     * @param      string $toType   One of the class type constants
     * @return string          translated name of the field.
     * @throws PropelException - if the specified name could not be found in the fieldname mappings.
     */
    public static function translateFieldName($name, $fromType, $toType)
    {
        $toNames = CavesPeer::getFieldNames($toType);
        $key = isset(CavesPeer::$fieldKeys[$fromType][$name]) ? CavesPeer::$fieldKeys[$fromType][$name] : null;
        if ($key === null) {
            throw new PropelException("'$name' could not be found in the field names of type '$fromType'. These are: " . print_r(CavesPeer::$fieldKeys[$fromType], true));
        }

        return $toNames[$key];
    }

    /**
     * Returns an array of field names.
     *
     * @param      string $type The type of fieldnames to return:
     *                      One of the class type constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME
     *                      BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM
     * @return array           A list of field names
     * @throws PropelException - if the type is not valid.
     */
    public static function getFieldNames($type = BasePeer::TYPE_PHPNAME)
    {
        if (!array_key_exists($type, CavesPeer::$fieldNames)) {
            throw new PropelException('Method getFieldNames() expects the parameter $type to be one of the class constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME, BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM. ' . $type . ' was given.');
        }

        return CavesPeer::$fieldNames[$type];
    }

    /**
     * Gets the list of values for all ENUM columns
     * @return array
     */
    public static function getValueSets()
    {
      return CavesPeer::$enumValueSets;
    }

    /**
     * Gets the list of values for an ENUM column
     *
     * @param string $colname The ENUM column name.
     *
     * @return array list of possible values for the column
     */
    public static function getValueSet($colname)
    {
        $valueSets = CavesPeer::getValueSets();

        if (!isset($valueSets[$colname])) {
            throw new PropelException(sprintf('Column "%s" has no ValueSet.', $colname));
        }

        return $valueSets[$colname];
    }

    /**
     * Gets the SQL value for the ENUM column value
     *
     * @param string $colname ENUM column name.
     * @param string $enumVal ENUM value.
     *
     * @return int SQL value
     */
    public static function getSqlValueForEnum($colname, $enumVal)
    {
        $values = CavesPeer::getValueSet($colname);
        if (!in_array($enumVal, $values)) {
            throw new PropelException(sprintf('Value "%s" is not accepted in this enumerated column', $colname));
        }

        return array_search($enumVal, $values);
    }

    /**
     * Convenience method which changes table.column to alias.column.
     *
     * Using this method you can maintain SQL abstraction while using column aliases.
     * <code>
     *		$c->addAlias("alias1", TablePeer::TABLE_NAME);
     *		$c->addJoin(TablePeer::alias("alias1", TablePeer::PRIMARY_KEY_COLUMN), TablePeer::PRIMARY_KEY_COLUMN);
     * </code>
     * @param      string $alias The alias for the current table.
     * @param      string $column The column name for current table. (i.e. CavesPeer::COLUMN_NAME).
     * @return string
     */
    public static function alias($alias, $column)
    {
        return str_replace(CavesPeer::TABLE_NAME.'.', $alias.'.', $column);
    }

    /**
     * Add all the columns needed to create a new object.
     *
     * Note: any columns that were marked with lazyLoad="true" in the
     * XML schema will not be added to the select list and only loaded
     * on demand.
     *
     * @param      Criteria $criteria object containing the columns to add.
     * @param      string   $alias    optional table alias
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function addSelectColumns(Criteria $criteria, $alias = null)
    {
        if (null === $alias) {
            $criteria->addSelectColumn(CavesPeer::ID);
            $criteria->addSelectColumn(CavesPeer::NAME);
            $criteria->addSelectColumn(CavesPeer::TYPE_ID);
            $criteria->addSelectColumn(CavesPeer::IDENTIFICATION_CODE);
            $criteria->addSelectColumn(CavesPeer::DESCRIPTION);
            $criteria->addSelectColumn(CavesPeer::USER_ID);
            $criteria->addSelectColumn(CavesPeer::OTHER_TOPONYMS);
            $criteria->addSelectColumn(CavesPeer::ROCK_TYPE_ID);
            $criteria->addSelectColumn(CavesPeer::ROCK_AGE);
            $criteria->addSelectColumn(CavesPeer::HYDROGRAPHIC_BASIN);
            $criteria->addSelectColumn(CavesPeer::VALLEY);
            $criteria->addSelectColumn(CavesPeer::TRIBUTARY_RIVER);
            $criteria->addSelectColumn(CavesPeer::CLOSEST_ADDRESS);
            $criteria->addSelectColumn(CavesPeer::IS_SHOW_CAVE);
            $criteria->addSelectColumn(CavesPeer::SHOW_CAVE_LENGTH);
            $criteria->addSelectColumn(CavesPeer::WEBSITE);
            $criteria->addSelectColumn(CavesPeer::LAND_REGISTRY_NUMBER);
            $criteria->addSelectColumn(CavesPeer::REGION);
            $criteria->addSelectColumn(CavesPeer::DEPTH);
            $criteria->addSelectColumn(CavesPeer::SURVEYED_LENGTH);
            $criteria->addSelectColumn(CavesPeer::DISCOVERY_DATE);
            $criteria->addSelectColumn(CavesPeer::DISCOVERER);
            $criteria->addSelectColumn(CavesPeer::VOLUME);
            $criteria->addSelectColumn(CavesPeer::POSITIVE_DEPTH);
            $criteria->addSelectColumn(CavesPeer::NEGATIVE_DEPTH);
            $criteria->addSelectColumn(CavesPeer::RAMIFICATION_INDEX);
            $criteria->addSelectColumn(CavesPeer::REAL_EXTENSION);
            $criteria->addSelectColumn(CavesPeer::CAVE_AGE);
            $criteria->addSelectColumn(CavesPeer::PROJECTED_EXTENSION);
            $criteria->addSelectColumn(CavesPeer::EXPLORATION_STATUS);
            $criteria->addSelectColumn(CavesPeer::PROTECTION_CLASS);
            $criteria->addSelectColumn(CavesPeer::POTENTIAL_DEPTH);
        } else {
            $criteria->addSelectColumn($alias . '.id');
            $criteria->addSelectColumn($alias . '.name');
            $criteria->addSelectColumn($alias . '.type_id');
            $criteria->addSelectColumn($alias . '.identification_code');
            $criteria->addSelectColumn($alias . '.description');
            $criteria->addSelectColumn($alias . '.user_id');
            $criteria->addSelectColumn($alias . '.other_toponyms');
            $criteria->addSelectColumn($alias . '.rock_type_id');
            $criteria->addSelectColumn($alias . '.rock_age');
            $criteria->addSelectColumn($alias . '.hydrographic_basin');
            $criteria->addSelectColumn($alias . '.valley');
            $criteria->addSelectColumn($alias . '.tributary_river');
            $criteria->addSelectColumn($alias . '.closest_address');
            $criteria->addSelectColumn($alias . '.is_show_cave');
            $criteria->addSelectColumn($alias . '.show_cave_length');
            $criteria->addSelectColumn($alias . '.website');
            $criteria->addSelectColumn($alias . '.land_registry_number');
            $criteria->addSelectColumn($alias . '.region');
            $criteria->addSelectColumn($alias . '.depth');
            $criteria->addSelectColumn($alias . '.surveyed_length');
            $criteria->addSelectColumn($alias . '.discovery_date');
            $criteria->addSelectColumn($alias . '.discoverer');
            $criteria->addSelectColumn($alias . '.volume');
            $criteria->addSelectColumn($alias . '.positive_depth');
            $criteria->addSelectColumn($alias . '.negative_depth');
            $criteria->addSelectColumn($alias . '.ramification_index');
            $criteria->addSelectColumn($alias . '.real_extension');
            $criteria->addSelectColumn($alias . '.cave_age');
            $criteria->addSelectColumn($alias . '.projected_extension');
            $criteria->addSelectColumn($alias . '.exploration_status');
            $criteria->addSelectColumn($alias . '.protection_class');
            $criteria->addSelectColumn($alias . '.potential_depth');
        }
    }

    /**
     * Returns the number of rows matching criteria.
     *
     * @param      Criteria $criteria
     * @param      boolean $distinct Whether to select only distinct columns; deprecated: use Criteria->setDistinct() instead.
     * @param      PropelPDO $con
     * @return int Number of matching rows.
     */
    public static function doCount(Criteria $criteria, $distinct = false, PropelPDO $con = null)
    {
        // we may modify criteria, so copy it first
        $criteria = clone $criteria;

        // We need to set the primary table name, since in the case that there are no WHERE columns
        // it will be impossible for the BasePeer::createSelectSql() method to determine which
        // tables go into the FROM clause.
        $criteria->setPrimaryTableName(CavesPeer::TABLE_NAME);

        if ($distinct && !in_array(Criteria::DISTINCT, $criteria->getSelectModifiers())) {
            $criteria->setDistinct();
        }

        if (!$criteria->hasSelectClause()) {
            CavesPeer::addSelectColumns($criteria);
        }

        $criteria->clearOrderByColumns(); // ORDER BY won't ever affect the count
        $criteria->setDbName(CavesPeer::DATABASE_NAME); // Set the correct dbName

        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }
        // BasePeer returns a PDOStatement
        $stmt = BasePeer::doCount($criteria, $con);

        if ($row = $stmt->fetch(PDO::FETCH_NUM)) {
            $count = (int) $row[0];
        } else {
            $count = 0; // no rows returned; we infer that means 0 matches.
        }
        $stmt->closeCursor();

        return $count;
    }
    /**
     * Selects one object from the DB.
     *
     * @param      Criteria $criteria object used to create the SELECT statement.
     * @param      PropelPDO $con
     * @return Caves
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doSelectOne(Criteria $criteria, PropelPDO $con = null)
    {
        $critcopy = clone $criteria;
        $critcopy->setLimit(1);
        $objects = CavesPeer::doSelect($critcopy, $con);
        if ($objects) {
            return $objects[0];
        }

        return null;
    }
    /**
     * Selects several row from the DB.
     *
     * @param      Criteria $criteria The Criteria object used to build the SELECT statement.
     * @param      PropelPDO $con
     * @return array           Array of selected Objects
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doSelect(Criteria $criteria, PropelPDO $con = null)
    {
        return CavesPeer::populateObjects(CavesPeer::doSelectStmt($criteria, $con));
    }
    /**
     * Prepares the Criteria object and uses the parent doSelect() method to execute a PDOStatement.
     *
     * Use this method directly if you want to work with an executed statement directly (for example
     * to perform your own object hydration).
     *
     * @param      Criteria $criteria The Criteria object used to build the SELECT statement.
     * @param      PropelPDO $con The connection to use
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     * @return PDOStatement The executed PDOStatement object.
     * @see        BasePeer::doSelect()
     */
    public static function doSelectStmt(Criteria $criteria, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        if (!$criteria->hasSelectClause()) {
            $criteria = clone $criteria;
            CavesPeer::addSelectColumns($criteria);
        }

        // Set the correct dbName
        $criteria->setDbName(CavesPeer::DATABASE_NAME);

        // BasePeer returns a PDOStatement
        return BasePeer::doSelect($criteria, $con);
    }
    /**
     * Adds an object to the instance pool.
     *
     * Propel keeps cached copies of objects in an instance pool when they are retrieved
     * from the database.  In some cases -- especially when you override doSelect*()
     * methods in your stub classes -- you may need to explicitly add objects
     * to the cache in order to ensure that the same objects are always returned by doSelect*()
     * and retrieveByPK*() calls.
     *
     * @param Caves $obj A Caves object.
     * @param      string $key (optional) key to use for instance map (for performance boost if key was already calculated externally).
     */
    public static function addInstanceToPool($obj, $key = null)
    {
        if (Propel::isInstancePoolingEnabled()) {
            if ($key === null) {
                $key = (string) $obj->getId();
            } // if key === null
            CavesPeer::$instances[$key] = $obj;
        }
    }

    /**
     * Removes an object from the instance pool.
     *
     * Propel keeps cached copies of objects in an instance pool when they are retrieved
     * from the database.  In some cases -- especially when you override doDelete
     * methods in your stub classes -- you may need to explicitly remove objects
     * from the cache in order to prevent returning objects that no longer exist.
     *
     * @param      mixed $value A Caves object or a primary key value.
     *
     * @return void
     * @throws PropelException - if the value is invalid.
     */
    public static function removeInstanceFromPool($value)
    {
        if (Propel::isInstancePoolingEnabled() && $value !== null) {
            if (is_object($value) && $value instanceof Caves) {
                $key = (string) $value->getId();
            } elseif (is_scalar($value)) {
                // assume we've been passed a primary key
                $key = (string) $value;
            } else {
                $e = new PropelException("Invalid value passed to removeInstanceFromPool().  Expected primary key or Caves object; got " . (is_object($value) ? get_class($value) . ' object.' : var_export($value,true)));
                throw $e;
            }

            unset(CavesPeer::$instances[$key]);
        }
    } // removeInstanceFromPool()

    /**
     * Retrieves a string version of the primary key from the DB resultset row that can be used to uniquely identify a row in this table.
     *
     * For tables with a single-column primary key, that simple pkey value will be returned.  For tables with
     * a multi-column primary key, a serialize()d version of the primary key will be returned.
     *
     * @param      string $key The key (@see getPrimaryKeyHash()) for this instance.
     * @return Caves Found object or null if 1) no instance exists for specified key or 2) instance pooling has been disabled.
     * @see        getPrimaryKeyHash()
     */
    public static function getInstanceFromPool($key)
    {
        if (Propel::isInstancePoolingEnabled()) {
            if (isset(CavesPeer::$instances[$key])) {
                return CavesPeer::$instances[$key];
            }
        }

        return null; // just to be explicit
    }

    /**
     * Clear the instance pool.
     *
     * @return void
     */
    public static function clearInstancePool($and_clear_all_references = false)
    {
      if ($and_clear_all_references) {
        foreach (CavesPeer::$instances as $instance) {
          $instance->clearAllReferences(true);
        }
      }
        CavesPeer::$instances = array();
    }

    /**
     * Method to invalidate the instance pool of all tables related to caves
     * by a foreign key with ON DELETE CASCADE
     */
    public static function clearRelatedInstancePool()
    {
    }

    /**
     * Retrieves a string version of the primary key from the DB resultset row that can be used to uniquely identify a row in this table.
     *
     * For tables with a single-column primary key, that simple pkey value will be returned.  For tables with
     * a multi-column primary key, a serialize()d version of the primary key will be returned.
     *
     * @param      array $row PropelPDO resultset row.
     * @param      int $startcol The 0-based offset for reading from the resultset row.
     * @return string A string version of PK or null if the components of primary key in result array are all null.
     */
    public static function getPrimaryKeyHashFromRow($row, $startcol = 0)
    {
        // If the PK cannot be derived from the row, return null.
        if ($row[$startcol] === null) {
            return null;
        }

        return (string) $row[$startcol];
    }

    /**
     * Retrieves the primary key from the DB resultset row
     * For tables with a single-column primary key, that simple pkey value will be returned.  For tables with
     * a multi-column primary key, an array of the primary key columns will be returned.
     *
     * @param      array $row PropelPDO resultset row.
     * @param      int $startcol The 0-based offset for reading from the resultset row.
     * @return mixed The primary key of the row
     */
    public static function getPrimaryKeyFromRow($row, $startcol = 0)
    {

        return (string) $row[$startcol];
    }

    /**
     * The returned array will contain objects of the default type or
     * objects that inherit from the default.
     *
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function populateObjects(PDOStatement $stmt)
    {
        $results = array();

        // set the class once to avoid overhead in the loop
        $cls = CavesPeer::getOMClass();
        // populate the object(s)
        while ($row = $stmt->fetch(PDO::FETCH_NUM)) {
            $key = CavesPeer::getPrimaryKeyHashFromRow($row, 0);
            if (null !== ($obj = CavesPeer::getInstanceFromPool($key))) {
                // We no longer rehydrate the object, since this can cause data loss.
                // See http://www.propelorm.org/ticket/509
                // $obj->hydrate($row, 0, true); // rehydrate
                $results[] = $obj;
            } else {
                $obj = new $cls();
                $obj->hydrate($row);
                $results[] = $obj;
                CavesPeer::addInstanceToPool($obj, $key);
            } // if key exists
        }
        $stmt->closeCursor();

        return $results;
    }
    /**
     * Populates an object of the default type or an object that inherit from the default.
     *
     * @param      array $row PropelPDO resultset row.
     * @param      int $startcol The 0-based offset for reading from the resultset row.
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     * @return array (Caves object, last column rank)
     */
    public static function populateObject($row, $startcol = 0)
    {
        $key = CavesPeer::getPrimaryKeyHashFromRow($row, $startcol);
        if (null !== ($obj = CavesPeer::getInstanceFromPool($key))) {
            // We no longer rehydrate the object, since this can cause data loss.
            // See http://www.propelorm.org/ticket/509
            // $obj->hydrate($row, $startcol, true); // rehydrate
            $col = $startcol + CavesPeer::NUM_HYDRATE_COLUMNS;
        } else {
            $cls = CavesPeer::OM_CLASS;
            $obj = new $cls();
            $col = $obj->hydrate($row, $startcol);
            CavesPeer::addInstanceToPool($obj, $key);
        }

        return array($obj, $col);
    }

    /**
     * Returns the TableMap related to this peer.
     * This method is not needed for general use but a specific application could have a need.
     * @return TableMap
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function getTableMap()
    {
        return Propel::getDatabaseMap(CavesPeer::DATABASE_NAME)->getTable(CavesPeer::TABLE_NAME);
    }

    /**
     * Add a TableMap instance to the database for this peer class.
     */
    public static function buildTableMap()
    {
      $dbMap = Propel::getDatabaseMap(BaseCavesPeer::DATABASE_NAME);
      if (!$dbMap->hasTable(BaseCavesPeer::TABLE_NAME)) {
        $dbMap->addTableObject(new \CavesTableMap());
      }
    }

    /**
     * The class that the Peer will make instances of.
     *
     *
     * @return string ClassName
     */
    public static function getOMClass($row = 0, $colnum = 0)
    {
        return CavesPeer::OM_CLASS;
    }

    /**
     * Performs an INSERT on the database, given a Caves or Criteria object.
     *
     * @param      mixed $values Criteria or Caves object containing data that is used to create the INSERT statement.
     * @param      PropelPDO $con the PropelPDO connection to use
     * @return mixed           The new primary key.
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doInsert($values, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        if ($values instanceof Criteria) {
            $criteria = clone $values; // rename for clarity
        } else {
            $criteria = $values->buildCriteria(); // build Criteria from Caves object
        }

        if ($criteria->containsKey(CavesPeer::ID) && $criteria->keyContainsValue(CavesPeer::ID) ) {
            throw new PropelException('Cannot insert a value for auto-increment primary key ('.CavesPeer::ID.')');
        }


        // Set the correct dbName
        $criteria->setDbName(CavesPeer::DATABASE_NAME);

        try {
            // use transaction because $criteria could contain info
            // for more than one table (I guess, conceivably)
            $con->beginTransaction();
            $pk = BasePeer::doInsert($criteria, $con);
            $con->commit();
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }

        return $pk;
    }

    /**
     * Performs an UPDATE on the database, given a Caves or Criteria object.
     *
     * @param      mixed $values Criteria or Caves object containing data that is used to create the UPDATE statement.
     * @param      PropelPDO $con The connection to use (specify PropelPDO connection object to exert more control over transactions).
     * @return int             The number of affected rows (if supported by underlying database driver).
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doUpdate($values, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        $selectCriteria = new Criteria(CavesPeer::DATABASE_NAME);

        if ($values instanceof Criteria) {
            $criteria = clone $values; // rename for clarity

            $comparison = $criteria->getComparison(CavesPeer::ID);
            $value = $criteria->remove(CavesPeer::ID);
            if ($value) {
                $selectCriteria->add(CavesPeer::ID, $value, $comparison);
            } else {
                $selectCriteria->setPrimaryTableName(CavesPeer::TABLE_NAME);
            }

        } else { // $values is Caves object
            $criteria = $values->buildCriteria(); // gets full criteria
            $selectCriteria = $values->buildPkeyCriteria(); // gets criteria w/ primary key(s)
        }

        // set the correct dbName
        $criteria->setDbName(CavesPeer::DATABASE_NAME);

        return BasePeer::doUpdate($selectCriteria, $criteria, $con);
    }

    /**
     * Deletes all rows from the caves table.
     *
     * @param      PropelPDO $con the connection to use
     * @return int             The number of affected rows (if supported by underlying database driver).
     * @throws PropelException
     */
    public static function doDeleteAll(PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }
        $affectedRows = 0; // initialize var to track total num of affected rows
        try {
            // use transaction because $criteria could contain info
            // for more than one table or we could emulating ON DELETE CASCADE, etc.
            $con->beginTransaction();
            $affectedRows += BasePeer::doDeleteAll(CavesPeer::TABLE_NAME, $con, CavesPeer::DATABASE_NAME);
            // Because this db requires some delete cascade/set null emulation, we have to
            // clear the cached instance *after* the emulation has happened (since
            // instances get re-added by the select statement contained therein).
            CavesPeer::clearInstancePool();
            CavesPeer::clearRelatedInstancePool();
            $con->commit();

            return $affectedRows;
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }
    }

    /**
     * Performs a DELETE on the database, given a Caves or Criteria object OR a primary key value.
     *
     * @param      mixed $values Criteria or Caves object or primary key or array of primary keys
     *              which is used to create the DELETE statement
     * @param      PropelPDO $con the connection to use
     * @return int The number of affected rows (if supported by underlying database driver).  This includes CASCADE-related rows
     *				if supported by native driver or if emulated using Propel.
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
     public static function doDelete($values, PropelPDO $con = null)
     {
        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        if ($values instanceof Criteria) {
            // invalidate the cache for all objects of this type, since we have no
            // way of knowing (without running a query) what objects should be invalidated
            // from the cache based on this Criteria.
            CavesPeer::clearInstancePool();
            // rename for clarity
            $criteria = clone $values;
        } elseif ($values instanceof Caves) { // it's a model object
            // invalidate the cache for this single object
            CavesPeer::removeInstanceFromPool($values);
            // create criteria based on pk values
            $criteria = $values->buildPkeyCriteria();
        } else { // it's a primary key, or an array of pks
            $criteria = new Criteria(CavesPeer::DATABASE_NAME);
            $criteria->add(CavesPeer::ID, (array) $values, Criteria::IN);
            // invalidate the cache for this object(s)
            foreach ((array) $values as $singleval) {
                CavesPeer::removeInstanceFromPool($singleval);
            }
        }

        // Set the correct dbName
        $criteria->setDbName(CavesPeer::DATABASE_NAME);

        $affectedRows = 0; // initialize var to track total num of affected rows

        try {
            // use transaction because $criteria could contain info
            // for more than one table or we could emulating ON DELETE CASCADE, etc.
            $con->beginTransaction();

            $affectedRows += BasePeer::doDelete($criteria, $con);
            CavesPeer::clearRelatedInstancePool();
            $con->commit();

            return $affectedRows;
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }
    }

    /**
     * Validates all modified columns of given Caves object.
     * If parameter $columns is either a single column name or an array of column names
     * than only those columns are validated.
     *
     * NOTICE: This does not apply to primary or foreign keys for now.
     *
     * @param Caves $obj The object to validate.
     * @param      mixed $cols Column name or array of column names.
     *
     * @return mixed TRUE if all columns are valid or the error message of the first invalid column.
     */
    public static function doValidate($obj, $cols = null)
    {
        $columns = array();

        if ($cols) {
            $dbMap = Propel::getDatabaseMap(CavesPeer::DATABASE_NAME);
            $tableMap = $dbMap->getTable(CavesPeer::TABLE_NAME);

            if (! is_array($cols)) {
                $cols = array($cols);
            }

            foreach ($cols as $colName) {
                if ($tableMap->hasColumn($colName)) {
                    $get = 'get' . $tableMap->getColumn($colName)->getPhpName();
                    $columns[$colName] = $obj->$get();
                }
            }
        } else {

        }

        return BasePeer::doValidate(CavesPeer::DATABASE_NAME, CavesPeer::TABLE_NAME, $columns);
    }

    /**
     * Retrieve a single object by pkey.
     *
     * @param string $pk the primary key.
     * @param      PropelPDO $con the connection to use
     * @return Caves
     */
    public static function retrieveByPK($pk, PropelPDO $con = null)
    {

        if (null !== ($obj = CavesPeer::getInstanceFromPool((string) $pk))) {
            return $obj;
        }

        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        $criteria = new Criteria(CavesPeer::DATABASE_NAME);
        $criteria->add(CavesPeer::ID, $pk);

        $v = CavesPeer::doSelect($criteria, $con);

        return !empty($v) > 0 ? $v[0] : null;
    }

    /**
     * Retrieve multiple objects by pkey.
     *
     * @param      array $pks List of primary keys
     * @param      PropelPDO $con the connection to use
     * @return Caves[]
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function retrieveByPKs($pks, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(CavesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        $objs = null;
        if (empty($pks)) {
            $objs = array();
        } else {
            $criteria = new Criteria(CavesPeer::DATABASE_NAME);
            $criteria->add(CavesPeer::ID, $pks, Criteria::IN);
            $objs = CavesPeer::doSelect($criteria, $con);
        }

        return $objs;
    }

} // BaseCavesPeer

// This is the static code needed to register the TableMap for this table with the main Propel class.
//
BaseCavesPeer::buildTableMap();

