<?php


/**
 * Base static class for performing query and update operations on the 'pointsxx' table.
 *
 *
 *
 * @package propel.generator.speogis.om
 */
abstract class BasePointsxxPeer
{

    /** the default database name for this class */
    const DATABASE_NAME = 'speogis';

    /** the table name for this class */
    const TABLE_NAME = 'pointsxx';

    /** the related Propel class for this table */
    const OM_CLASS = 'Pointsxx';

    /** the related TableMap class for this table */
    const TM_CLASS = 'PointsxxTableMap';

    /** The total number of columns. */
    const NUM_COLUMNS = 18;

    /** The number of lazy-loaded columns. */
    const NUM_LAZY_LOAD_COLUMNS = 0;

    /** The number of columns to hydrate (NUM_COLUMNS - NUM_LAZY_LOAD_COLUMNS) */
    const NUM_HYDRATE_COLUMNS = 18;

    /** the column name for the id field */
    const ID = 'pointsxx.id';

    /** the column name for the lat field */
    const LAT = 'pointsxx.lat';

    /** the column name for the long field */
    const LONG = 'pointsxx.long';

    /** the column name for the elevation field */
    const ELEVATION = 'pointsxx.elevation';

    /** the column name for the coords field */
    const COORDS = 'pointsxx.coords';

    /** the column name for the gpx_name field */
    const GPX_NAME = 'pointsxx.gpx_name';

    /** the column name for the gpx_sym field */
    const GPX_SYM = 'pointsxx.gpx_sym';

    /** the column name for the gpx_type field */
    const GPX_TYPE = 'pointsxx.gpx_type';

    /** the column name for the gpx_cmt field */
    const GPX_CMT = 'pointsxx.gpx_cmt';

    /** the column name for the gpx_sat field */
    const GPX_SAT = 'pointsxx.gpx_sat';

    /** the column name for the gpx_fix field */
    const GPX_FIX = 'pointsxx.gpx_fix';

    /** the column name for the gpx_time field */
    const GPX_TIME = 'pointsxx.gpx_time';

    /** the column name for the _type field */
    const _TYPE = 'pointsxx._type';

    /** the column name for the _details field */
    const _DETAILS = 'pointsxx._details';

    /** the column name for the added_by_user_id field */
    const ADDED_BY_USER_ID = 'pointsxx.added_by_user_id';

    /** the column name for the add_time field */
    const ADD_TIME = 'pointsxx.add_time';

    /** the column name for the _id_point_type field */
    const _ID_POINT_TYPE = 'pointsxx._id_point_type';

    /** the column name for the spatial_geometry field */
    const SPATIAL_GEOMETRY = 'pointsxx.spatial_geometry';

    /** The default string format for model objects of the related table **/
    const DEFAULT_STRING_FORMAT = 'YAML';

    /**
     * An identity map to hold any loaded instances of Pointsxx objects.
     * This must be public so that other peer classes can access this when hydrating from JOIN
     * queries.
     * @var        array Pointsxx[]
     */
    public static $instances = array();


    /**
     * holds an array of fieldnames
     *
     * first dimension keys are the type constants
     * e.g. PointsxxPeer::$fieldNames[PointsxxPeer::TYPE_PHPNAME][0] = 'Id'
     */
    protected static $fieldNames = array (
        BasePeer::TYPE_PHPNAME => array ('Id', 'Lat', 'Long', 'Elevation', 'Coords', 'GpxName', 'GpxSym', 'GpxType', 'GpxCmt', 'GpxSat', 'GpxFix', 'GpxTime', 'Type', 'Details', 'AddedByUserId', 'AddTime', 'IdPointType', 'SpatialGeometry', ),
        BasePeer::TYPE_STUDLYPHPNAME => array ('id', 'lat', 'long', 'elevation', 'coords', 'gpxName', 'gpxSym', 'gpxType', 'gpxCmt', 'gpxSat', 'gpxFix', 'gpxTime', 'type', 'details', 'addedByUserId', 'addTime', 'idPointType', 'spatialGeometry', ),
        BasePeer::TYPE_COLNAME => array (PointsxxPeer::ID, PointsxxPeer::LAT, PointsxxPeer::LONG, PointsxxPeer::ELEVATION, PointsxxPeer::COORDS, PointsxxPeer::GPX_NAME, PointsxxPeer::GPX_SYM, PointsxxPeer::GPX_TYPE, PointsxxPeer::GPX_CMT, PointsxxPeer::GPX_SAT, PointsxxPeer::GPX_FIX, PointsxxPeer::GPX_TIME, PointsxxPeer::_TYPE, PointsxxPeer::_DETAILS, PointsxxPeer::ADDED_BY_USER_ID, PointsxxPeer::ADD_TIME, PointsxxPeer::_ID_POINT_TYPE, PointsxxPeer::SPATIAL_GEOMETRY, ),
        BasePeer::TYPE_RAW_COLNAME => array ('ID', 'LAT', 'LONG', 'ELEVATION', 'COORDS', 'GPX_NAME', 'GPX_SYM', 'GPX_TYPE', 'GPX_CMT', 'GPX_SAT', 'GPX_FIX', 'GPX_TIME', '_TYPE', '_DETAILS', 'ADDED_BY_USER_ID', 'ADD_TIME', '_ID_POINT_TYPE', 'SPATIAL_GEOMETRY', ),
        BasePeer::TYPE_FIELDNAME => array ('id', 'lat', 'long', 'elevation', 'coords', 'gpx_name', 'gpx_sym', 'gpx_type', 'gpx_cmt', 'gpx_sat', 'gpx_fix', 'gpx_time', '_type', '_details', 'added_by_user_id', 'add_time', '_id_point_type', 'spatial_geometry', ),
        BasePeer::TYPE_NUM => array (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, )
    );

    /**
     * holds an array of keys for quick access to the fieldnames array
     *
     * first dimension keys are the type constants
     * e.g. PointsxxPeer::$fieldNames[BasePeer::TYPE_PHPNAME]['Id'] = 0
     */
    protected static $fieldKeys = array (
        BasePeer::TYPE_PHPNAME => array ('Id' => 0, 'Lat' => 1, 'Long' => 2, 'Elevation' => 3, 'Coords' => 4, 'GpxName' => 5, 'GpxSym' => 6, 'GpxType' => 7, 'GpxCmt' => 8, 'GpxSat' => 9, 'GpxFix' => 10, 'GpxTime' => 11, 'Type' => 12, 'Details' => 13, 'AddedByUserId' => 14, 'AddTime' => 15, 'IdPointType' => 16, 'SpatialGeometry' => 17, ),
        BasePeer::TYPE_STUDLYPHPNAME => array ('id' => 0, 'lat' => 1, 'long' => 2, 'elevation' => 3, 'coords' => 4, 'gpxName' => 5, 'gpxSym' => 6, 'gpxType' => 7, 'gpxCmt' => 8, 'gpxSat' => 9, 'gpxFix' => 10, 'gpxTime' => 11, 'type' => 12, 'details' => 13, 'addedByUserId' => 14, 'addTime' => 15, 'idPointType' => 16, 'spatialGeometry' => 17, ),
        BasePeer::TYPE_COLNAME => array (PointsxxPeer::ID => 0, PointsxxPeer::LAT => 1, PointsxxPeer::LONG => 2, PointsxxPeer::ELEVATION => 3, PointsxxPeer::COORDS => 4, PointsxxPeer::GPX_NAME => 5, PointsxxPeer::GPX_SYM => 6, PointsxxPeer::GPX_TYPE => 7, PointsxxPeer::GPX_CMT => 8, PointsxxPeer::GPX_SAT => 9, PointsxxPeer::GPX_FIX => 10, PointsxxPeer::GPX_TIME => 11, PointsxxPeer::_TYPE => 12, PointsxxPeer::_DETAILS => 13, PointsxxPeer::ADDED_BY_USER_ID => 14, PointsxxPeer::ADD_TIME => 15, PointsxxPeer::_ID_POINT_TYPE => 16, PointsxxPeer::SPATIAL_GEOMETRY => 17, ),
        BasePeer::TYPE_RAW_COLNAME => array ('ID' => 0, 'LAT' => 1, 'LONG' => 2, 'ELEVATION' => 3, 'COORDS' => 4, 'GPX_NAME' => 5, 'GPX_SYM' => 6, 'GPX_TYPE' => 7, 'GPX_CMT' => 8, 'GPX_SAT' => 9, 'GPX_FIX' => 10, 'GPX_TIME' => 11, '_TYPE' => 12, '_DETAILS' => 13, 'ADDED_BY_USER_ID' => 14, 'ADD_TIME' => 15, '_ID_POINT_TYPE' => 16, 'SPATIAL_GEOMETRY' => 17, ),
        BasePeer::TYPE_FIELDNAME => array ('id' => 0, 'lat' => 1, 'long' => 2, 'elevation' => 3, 'coords' => 4, 'gpx_name' => 5, 'gpx_sym' => 6, 'gpx_type' => 7, 'gpx_cmt' => 8, 'gpx_sat' => 9, 'gpx_fix' => 10, 'gpx_time' => 11, '_type' => 12, '_details' => 13, 'added_by_user_id' => 14, 'add_time' => 15, '_id_point_type' => 16, 'spatial_geometry' => 17, ),
        BasePeer::TYPE_NUM => array (0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, )
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
        $toNames = PointsxxPeer::getFieldNames($toType);
        $key = isset(PointsxxPeer::$fieldKeys[$fromType][$name]) ? PointsxxPeer::$fieldKeys[$fromType][$name] : null;
        if ($key === null) {
            throw new PropelException("'$name' could not be found in the field names of type '$fromType'. These are: " . print_r(PointsxxPeer::$fieldKeys[$fromType], true));
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
        if (!array_key_exists($type, PointsxxPeer::$fieldNames)) {
            throw new PropelException('Method getFieldNames() expects the parameter $type to be one of the class constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME, BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM. ' . $type . ' was given.');
        }

        return PointsxxPeer::$fieldNames[$type];
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
     * @param      string $column The column name for current table. (i.e. PointsxxPeer::COLUMN_NAME).
     * @return string
     */
    public static function alias($alias, $column)
    {
        return str_replace(PointsxxPeer::TABLE_NAME.'.', $alias.'.', $column);
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
            $criteria->addSelectColumn(PointsxxPeer::ID);
            $criteria->addSelectColumn(PointsxxPeer::LAT);
            $criteria->addSelectColumn(PointsxxPeer::LONG);
            $criteria->addSelectColumn(PointsxxPeer::ELEVATION);
            $criteria->addSelectColumn(PointsxxPeer::COORDS);
            $criteria->addSelectColumn(PointsxxPeer::GPX_NAME);
            $criteria->addSelectColumn(PointsxxPeer::GPX_SYM);
            $criteria->addSelectColumn(PointsxxPeer::GPX_TYPE);
            $criteria->addSelectColumn(PointsxxPeer::GPX_CMT);
            $criteria->addSelectColumn(PointsxxPeer::GPX_SAT);
            $criteria->addSelectColumn(PointsxxPeer::GPX_FIX);
            $criteria->addSelectColumn(PointsxxPeer::GPX_TIME);
            $criteria->addSelectColumn(PointsxxPeer::_TYPE);
            $criteria->addSelectColumn(PointsxxPeer::_DETAILS);
            $criteria->addSelectColumn(PointsxxPeer::ADDED_BY_USER_ID);
            $criteria->addSelectColumn(PointsxxPeer::ADD_TIME);
            $criteria->addSelectColumn(PointsxxPeer::_ID_POINT_TYPE);
            $criteria->addSelectColumn(PointsxxPeer::SPATIAL_GEOMETRY);
        } else {
            $criteria->addSelectColumn($alias . '.id');
            $criteria->addSelectColumn($alias . '.lat');
            $criteria->addSelectColumn($alias . '.long');
            $criteria->addSelectColumn($alias . '.elevation');
            $criteria->addSelectColumn($alias . '.coords');
            $criteria->addSelectColumn($alias . '.gpx_name');
            $criteria->addSelectColumn($alias . '.gpx_sym');
            $criteria->addSelectColumn($alias . '.gpx_type');
            $criteria->addSelectColumn($alias . '.gpx_cmt');
            $criteria->addSelectColumn($alias . '.gpx_sat');
            $criteria->addSelectColumn($alias . '.gpx_fix');
            $criteria->addSelectColumn($alias . '.gpx_time');
            $criteria->addSelectColumn($alias . '._type');
            $criteria->addSelectColumn($alias . '._details');
            $criteria->addSelectColumn($alias . '.added_by_user_id');
            $criteria->addSelectColumn($alias . '.add_time');
            $criteria->addSelectColumn($alias . '._id_point_type');
            $criteria->addSelectColumn($alias . '.spatial_geometry');
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
        $criteria->setPrimaryTableName(PointsxxPeer::TABLE_NAME);

        if ($distinct && !in_array(Criteria::DISTINCT, $criteria->getSelectModifiers())) {
            $criteria->setDistinct();
        }

        if (!$criteria->hasSelectClause()) {
            PointsxxPeer::addSelectColumns($criteria);
        }

        $criteria->clearOrderByColumns(); // ORDER BY won't ever affect the count
        $criteria->setDbName(PointsxxPeer::DATABASE_NAME); // Set the correct dbName

        if ($con === null) {
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return Pointsxx
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doSelectOne(Criteria $criteria, PropelPDO $con = null)
    {
        $critcopy = clone $criteria;
        $critcopy->setLimit(1);
        $objects = PointsxxPeer::doSelect($critcopy, $con);
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
        return PointsxxPeer::populateObjects(PointsxxPeer::doSelectStmt($criteria, $con));
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
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        if (!$criteria->hasSelectClause()) {
            $criteria = clone $criteria;
            PointsxxPeer::addSelectColumns($criteria);
        }

        // Set the correct dbName
        $criteria->setDbName(PointsxxPeer::DATABASE_NAME);

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
     * @param Pointsxx $obj A Pointsxx object.
     * @param      string $key (optional) key to use for instance map (for performance boost if key was already calculated externally).
     */
    public static function addInstanceToPool($obj, $key = null)
    {
        if (Propel::isInstancePoolingEnabled()) {
            if ($key === null) {
                $key = (string) $obj->getId();
            } // if key === null
            PointsxxPeer::$instances[$key] = $obj;
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
     * @param      mixed $value A Pointsxx object or a primary key value.
     *
     * @return void
     * @throws PropelException - if the value is invalid.
     */
    public static function removeInstanceFromPool($value)
    {
        if (Propel::isInstancePoolingEnabled() && $value !== null) {
            if (is_object($value) && $value instanceof Pointsxx) {
                $key = (string) $value->getId();
            } elseif (is_scalar($value)) {
                // assume we've been passed a primary key
                $key = (string) $value;
            } else {
                $e = new PropelException("Invalid value passed to removeInstanceFromPool().  Expected primary key or Pointsxx object; got " . (is_object($value) ? get_class($value) . ' object.' : var_export($value,true)));
                throw $e;
            }

            unset(PointsxxPeer::$instances[$key]);
        }
    } // removeInstanceFromPool()

    /**
     * Retrieves a string version of the primary key from the DB resultset row that can be used to uniquely identify a row in this table.
     *
     * For tables with a single-column primary key, that simple pkey value will be returned.  For tables with
     * a multi-column primary key, a serialize()d version of the primary key will be returned.
     *
     * @param      string $key The key (@see getPrimaryKeyHash()) for this instance.
     * @return Pointsxx Found object or null if 1) no instance exists for specified key or 2) instance pooling has been disabled.
     * @see        getPrimaryKeyHash()
     */
    public static function getInstanceFromPool($key)
    {
        if (Propel::isInstancePoolingEnabled()) {
            if (isset(PointsxxPeer::$instances[$key])) {
                return PointsxxPeer::$instances[$key];
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
        foreach (PointsxxPeer::$instances as $instance) {
          $instance->clearAllReferences(true);
        }
      }
        PointsxxPeer::$instances = array();
    }

    /**
     * Method to invalidate the instance pool of all tables related to pointsxx
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
        $cls = PointsxxPeer::getOMClass();
        // populate the object(s)
        while ($row = $stmt->fetch(PDO::FETCH_NUM)) {
            $key = PointsxxPeer::getPrimaryKeyHashFromRow($row, 0);
            if (null !== ($obj = PointsxxPeer::getInstanceFromPool($key))) {
                // We no longer rehydrate the object, since this can cause data loss.
                // See http://www.propelorm.org/ticket/509
                // $obj->hydrate($row, 0, true); // rehydrate
                $results[] = $obj;
            } else {
                $obj = new $cls();
                $obj->hydrate($row);
                $results[] = $obj;
                PointsxxPeer::addInstanceToPool($obj, $key);
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
     * @return array (Pointsxx object, last column rank)
     */
    public static function populateObject($row, $startcol = 0)
    {
        $key = PointsxxPeer::getPrimaryKeyHashFromRow($row, $startcol);
        if (null !== ($obj = PointsxxPeer::getInstanceFromPool($key))) {
            // We no longer rehydrate the object, since this can cause data loss.
            // See http://www.propelorm.org/ticket/509
            // $obj->hydrate($row, $startcol, true); // rehydrate
            $col = $startcol + PointsxxPeer::NUM_HYDRATE_COLUMNS;
        } else {
            $cls = PointsxxPeer::OM_CLASS;
            $obj = new $cls();
            $col = $obj->hydrate($row, $startcol);
            PointsxxPeer::addInstanceToPool($obj, $key);
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
        return Propel::getDatabaseMap(PointsxxPeer::DATABASE_NAME)->getTable(PointsxxPeer::TABLE_NAME);
    }

    /**
     * Add a TableMap instance to the database for this peer class.
     */
    public static function buildTableMap()
    {
      $dbMap = Propel::getDatabaseMap(BasePointsxxPeer::DATABASE_NAME);
      if (!$dbMap->hasTable(BasePointsxxPeer::TABLE_NAME)) {
        $dbMap->addTableObject(new \PointsxxTableMap());
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
        return PointsxxPeer::OM_CLASS;
    }

    /**
     * Performs an INSERT on the database, given a Pointsxx or Criteria object.
     *
     * @param      mixed $values Criteria or Pointsxx object containing data that is used to create the INSERT statement.
     * @param      PropelPDO $con the PropelPDO connection to use
     * @return mixed           The new primary key.
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doInsert($values, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        if ($values instanceof Criteria) {
            $criteria = clone $values; // rename for clarity
        } else {
            $criteria = $values->buildCriteria(); // build Criteria from Pointsxx object
        }


        // Set the correct dbName
        $criteria->setDbName(PointsxxPeer::DATABASE_NAME);

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
     * Performs an UPDATE on the database, given a Pointsxx or Criteria object.
     *
     * @param      mixed $values Criteria or Pointsxx object containing data that is used to create the UPDATE statement.
     * @param      PropelPDO $con The connection to use (specify PropelPDO connection object to exert more control over transactions).
     * @return int             The number of affected rows (if supported by underlying database driver).
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doUpdate($values, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        $selectCriteria = new Criteria(PointsxxPeer::DATABASE_NAME);

        if ($values instanceof Criteria) {
            $criteria = clone $values; // rename for clarity

            $comparison = $criteria->getComparison(PointsxxPeer::ID);
            $value = $criteria->remove(PointsxxPeer::ID);
            if ($value) {
                $selectCriteria->add(PointsxxPeer::ID, $value, $comparison);
            } else {
                $selectCriteria->setPrimaryTableName(PointsxxPeer::TABLE_NAME);
            }

        } else { // $values is Pointsxx object
            $criteria = $values->buildCriteria(); // gets full criteria
            $selectCriteria = $values->buildPkeyCriteria(); // gets criteria w/ primary key(s)
        }

        // set the correct dbName
        $criteria->setDbName(PointsxxPeer::DATABASE_NAME);

        return BasePeer::doUpdate($selectCriteria, $criteria, $con);
    }

    /**
     * Deletes all rows from the pointsxx table.
     *
     * @param      PropelPDO $con the connection to use
     * @return int             The number of affected rows (if supported by underlying database driver).
     * @throws PropelException
     */
    public static function doDeleteAll(PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }
        $affectedRows = 0; // initialize var to track total num of affected rows
        try {
            // use transaction because $criteria could contain info
            // for more than one table or we could emulating ON DELETE CASCADE, etc.
            $con->beginTransaction();
            $affectedRows += BasePeer::doDeleteAll(PointsxxPeer::TABLE_NAME, $con, PointsxxPeer::DATABASE_NAME);
            // Because this db requires some delete cascade/set null emulation, we have to
            // clear the cached instance *after* the emulation has happened (since
            // instances get re-added by the select statement contained therein).
            PointsxxPeer::clearInstancePool();
            PointsxxPeer::clearRelatedInstancePool();
            $con->commit();

            return $affectedRows;
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }
    }

    /**
     * Performs a DELETE on the database, given a Pointsxx or Criteria object OR a primary key value.
     *
     * @param      mixed $values Criteria or Pointsxx object or primary key or array of primary keys
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
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        if ($values instanceof Criteria) {
            // invalidate the cache for all objects of this type, since we have no
            // way of knowing (without running a query) what objects should be invalidated
            // from the cache based on this Criteria.
            PointsxxPeer::clearInstancePool();
            // rename for clarity
            $criteria = clone $values;
        } elseif ($values instanceof Pointsxx) { // it's a model object
            // invalidate the cache for this single object
            PointsxxPeer::removeInstanceFromPool($values);
            // create criteria based on pk values
            $criteria = $values->buildPkeyCriteria();
        } else { // it's a primary key, or an array of pks
            $criteria = new Criteria(PointsxxPeer::DATABASE_NAME);
            $criteria->add(PointsxxPeer::ID, (array) $values, Criteria::IN);
            // invalidate the cache for this object(s)
            foreach ((array) $values as $singleval) {
                PointsxxPeer::removeInstanceFromPool($singleval);
            }
        }

        // Set the correct dbName
        $criteria->setDbName(PointsxxPeer::DATABASE_NAME);

        $affectedRows = 0; // initialize var to track total num of affected rows

        try {
            // use transaction because $criteria could contain info
            // for more than one table or we could emulating ON DELETE CASCADE, etc.
            $con->beginTransaction();

            $affectedRows += BasePeer::doDelete($criteria, $con);
            PointsxxPeer::clearRelatedInstancePool();
            $con->commit();

            return $affectedRows;
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }
    }

    /**
     * Validates all modified columns of given Pointsxx object.
     * If parameter $columns is either a single column name or an array of column names
     * than only those columns are validated.
     *
     * NOTICE: This does not apply to primary or foreign keys for now.
     *
     * @param Pointsxx $obj The object to validate.
     * @param      mixed $cols Column name or array of column names.
     *
     * @return mixed TRUE if all columns are valid or the error message of the first invalid column.
     */
    public static function doValidate($obj, $cols = null)
    {
        $columns = array();

        if ($cols) {
            $dbMap = Propel::getDatabaseMap(PointsxxPeer::DATABASE_NAME);
            $tableMap = $dbMap->getTable(PointsxxPeer::TABLE_NAME);

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

        return BasePeer::doValidate(PointsxxPeer::DATABASE_NAME, PointsxxPeer::TABLE_NAME, $columns);
    }

    /**
     * Retrieve a single object by pkey.
     *
     * @param string $pk the primary key.
     * @param      PropelPDO $con the connection to use
     * @return Pointsxx
     */
    public static function retrieveByPK($pk, PropelPDO $con = null)
    {

        if (null !== ($obj = PointsxxPeer::getInstanceFromPool((string) $pk))) {
            return $obj;
        }

        if ($con === null) {
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        $criteria = new Criteria(PointsxxPeer::DATABASE_NAME);
        $criteria->add(PointsxxPeer::ID, $pk);

        $v = PointsxxPeer::doSelect($criteria, $con);

        return !empty($v) > 0 ? $v[0] : null;
    }

    /**
     * Retrieve multiple objects by pkey.
     *
     * @param      array $pks List of primary keys
     * @param      PropelPDO $con the connection to use
     * @return Pointsxx[]
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function retrieveByPKs($pks, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(PointsxxPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        $objs = null;
        if (empty($pks)) {
            $objs = array();
        } else {
            $criteria = new Criteria(PointsxxPeer::DATABASE_NAME);
            $criteria->add(PointsxxPeer::ID, $pks, Criteria::IN);
            $objs = PointsxxPeer::doSelect($criteria, $con);
        }

        return $objs;
    }

} // BasePointsxxPeer

// This is the static code needed to register the TableMap for this table with the main Propel class.
//
BasePointsxxPeer::buildTableMap();

