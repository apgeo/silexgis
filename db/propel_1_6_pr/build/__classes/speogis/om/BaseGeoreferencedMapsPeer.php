<?php


/**
 * Base static class for performing query and update operations on the 'georeferenced_maps' table.
 *
 *
 *
 * @package propel.generator.speogis.om
 */
abstract class BaseGeoreferencedMapsPeer
{

    /** the default database name for this class */
    const DATABASE_NAME = 'speogis';

    /** the table name for this class */
    const TABLE_NAME = 'georeferenced_maps';

    /** the related Propel class for this table */
    const OM_CLASS = 'GeoreferencedMaps';

    /** the related TableMap class for this table */
    const TM_CLASS = 'GeoreferencedMapsTableMap';

    /** The total number of columns. */
    const NUM_COLUMNS = 9;

    /** The number of lazy-loaded columns. */
    const NUM_LAZY_LOAD_COLUMNS = 0;

    /** The number of columns to hydrate (NUM_COLUMNS - NUM_LAZY_LOAD_COLUMNS) */
    const NUM_HYDRATE_COLUMNS = 9;

    /** the column name for the id field */
    const ID = 'georeferenced_maps.id';

    /** the column name for the description field */
    const DESCRIPTION = 'georeferenced_maps.description';

    /** the column name for the boundary_north field */
    const BOUNDARY_NORTH = 'georeferenced_maps.boundary_north';

    /** the column name for the boundary_east field */
    const BOUNDARY_EAST = 'georeferenced_maps.boundary_east';

    /** the column name for the boundary_south field */
    const BOUNDARY_SOUTH = 'georeferenced_maps.boundary_south';

    /** the column name for the boundary_west field */
    const BOUNDARY_WEST = 'georeferenced_maps.boundary_west';

    /** the column name for the image_id field */
    const IMAGE_ID = 'georeferenced_maps.image_id';

    /** the column name for the enabled field */
    const ENABLED = 'georeferenced_maps.enabled';

    /** the column name for the title field */
    const TITLE = 'georeferenced_maps.title';

    /** The default string format for model objects of the related table **/
    const DEFAULT_STRING_FORMAT = 'YAML';

    /**
     * An identity map to hold any loaded instances of GeoreferencedMaps objects.
     * This must be public so that other peer classes can access this when hydrating from JOIN
     * queries.
     * @var        array GeoreferencedMaps[]
     */
    public static $instances = array();


    /**
     * holds an array of fieldnames
     *
     * first dimension keys are the type constants
     * e.g. GeoreferencedMapsPeer::$fieldNames[GeoreferencedMapsPeer::TYPE_PHPNAME][0] = 'Id'
     */
    protected static $fieldNames = array (
        BasePeer::TYPE_PHPNAME => array ('Id', 'Description', 'BoundaryNorth', 'BoundaryEast', 'BoundarySouth', 'BoundaryWest', 'ImageId', 'Enabled', 'Title', ),
        BasePeer::TYPE_STUDLYPHPNAME => array ('id', 'description', 'boundaryNorth', 'boundaryEast', 'boundarySouth', 'boundaryWest', 'imageId', 'enabled', 'title', ),
        BasePeer::TYPE_COLNAME => array (GeoreferencedMapsPeer::ID, GeoreferencedMapsPeer::DESCRIPTION, GeoreferencedMapsPeer::BOUNDARY_NORTH, GeoreferencedMapsPeer::BOUNDARY_EAST, GeoreferencedMapsPeer::BOUNDARY_SOUTH, GeoreferencedMapsPeer::BOUNDARY_WEST, GeoreferencedMapsPeer::IMAGE_ID, GeoreferencedMapsPeer::ENABLED, GeoreferencedMapsPeer::TITLE, ),
        BasePeer::TYPE_RAW_COLNAME => array ('ID', 'DESCRIPTION', 'BOUNDARY_NORTH', 'BOUNDARY_EAST', 'BOUNDARY_SOUTH', 'BOUNDARY_WEST', 'IMAGE_ID', 'ENABLED', 'TITLE', ),
        BasePeer::TYPE_FIELDNAME => array ('id', 'description', 'boundary_north', 'boundary_east', 'boundary_south', 'boundary_west', 'image_id', 'enabled', 'title', ),
        BasePeer::TYPE_NUM => array (0, 1, 2, 3, 4, 5, 6, 7, 8, )
    );

    /**
     * holds an array of keys for quick access to the fieldnames array
     *
     * first dimension keys are the type constants
     * e.g. GeoreferencedMapsPeer::$fieldNames[BasePeer::TYPE_PHPNAME]['Id'] = 0
     */
    protected static $fieldKeys = array (
        BasePeer::TYPE_PHPNAME => array ('Id' => 0, 'Description' => 1, 'BoundaryNorth' => 2, 'BoundaryEast' => 3, 'BoundarySouth' => 4, 'BoundaryWest' => 5, 'ImageId' => 6, 'Enabled' => 7, 'Title' => 8, ),
        BasePeer::TYPE_STUDLYPHPNAME => array ('id' => 0, 'description' => 1, 'boundaryNorth' => 2, 'boundaryEast' => 3, 'boundarySouth' => 4, 'boundaryWest' => 5, 'imageId' => 6, 'enabled' => 7, 'title' => 8, ),
        BasePeer::TYPE_COLNAME => array (GeoreferencedMapsPeer::ID => 0, GeoreferencedMapsPeer::DESCRIPTION => 1, GeoreferencedMapsPeer::BOUNDARY_NORTH => 2, GeoreferencedMapsPeer::BOUNDARY_EAST => 3, GeoreferencedMapsPeer::BOUNDARY_SOUTH => 4, GeoreferencedMapsPeer::BOUNDARY_WEST => 5, GeoreferencedMapsPeer::IMAGE_ID => 6, GeoreferencedMapsPeer::ENABLED => 7, GeoreferencedMapsPeer::TITLE => 8, ),
        BasePeer::TYPE_RAW_COLNAME => array ('ID' => 0, 'DESCRIPTION' => 1, 'BOUNDARY_NORTH' => 2, 'BOUNDARY_EAST' => 3, 'BOUNDARY_SOUTH' => 4, 'BOUNDARY_WEST' => 5, 'IMAGE_ID' => 6, 'ENABLED' => 7, 'TITLE' => 8, ),
        BasePeer::TYPE_FIELDNAME => array ('id' => 0, 'description' => 1, 'boundary_north' => 2, 'boundary_east' => 3, 'boundary_south' => 4, 'boundary_west' => 5, 'image_id' => 6, 'enabled' => 7, 'title' => 8, ),
        BasePeer::TYPE_NUM => array (0, 1, 2, 3, 4, 5, 6, 7, 8, )
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
        $toNames = GeoreferencedMapsPeer::getFieldNames($toType);
        $key = isset(GeoreferencedMapsPeer::$fieldKeys[$fromType][$name]) ? GeoreferencedMapsPeer::$fieldKeys[$fromType][$name] : null;
        if ($key === null) {
            throw new PropelException("'$name' could not be found in the field names of type '$fromType'. These are: " . print_r(GeoreferencedMapsPeer::$fieldKeys[$fromType], true));
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
        if (!array_key_exists($type, GeoreferencedMapsPeer::$fieldNames)) {
            throw new PropelException('Method getFieldNames() expects the parameter $type to be one of the class constants BasePeer::TYPE_PHPNAME, BasePeer::TYPE_STUDLYPHPNAME, BasePeer::TYPE_COLNAME, BasePeer::TYPE_FIELDNAME, BasePeer::TYPE_NUM. ' . $type . ' was given.');
        }

        return GeoreferencedMapsPeer::$fieldNames[$type];
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
     * @param      string $column The column name for current table. (i.e. GeoreferencedMapsPeer::COLUMN_NAME).
     * @return string
     */
    public static function alias($alias, $column)
    {
        return str_replace(GeoreferencedMapsPeer::TABLE_NAME.'.', $alias.'.', $column);
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
            $criteria->addSelectColumn(GeoreferencedMapsPeer::ID);
            $criteria->addSelectColumn(GeoreferencedMapsPeer::DESCRIPTION);
            $criteria->addSelectColumn(GeoreferencedMapsPeer::BOUNDARY_NORTH);
            $criteria->addSelectColumn(GeoreferencedMapsPeer::BOUNDARY_EAST);
            $criteria->addSelectColumn(GeoreferencedMapsPeer::BOUNDARY_SOUTH);
            $criteria->addSelectColumn(GeoreferencedMapsPeer::BOUNDARY_WEST);
            $criteria->addSelectColumn(GeoreferencedMapsPeer::IMAGE_ID);
            $criteria->addSelectColumn(GeoreferencedMapsPeer::ENABLED);
            $criteria->addSelectColumn(GeoreferencedMapsPeer::TITLE);
        } else {
            $criteria->addSelectColumn($alias . '.id');
            $criteria->addSelectColumn($alias . '.description');
            $criteria->addSelectColumn($alias . '.boundary_north');
            $criteria->addSelectColumn($alias . '.boundary_east');
            $criteria->addSelectColumn($alias . '.boundary_south');
            $criteria->addSelectColumn($alias . '.boundary_west');
            $criteria->addSelectColumn($alias . '.image_id');
            $criteria->addSelectColumn($alias . '.enabled');
            $criteria->addSelectColumn($alias . '.title');
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
        $criteria->setPrimaryTableName(GeoreferencedMapsPeer::TABLE_NAME);

        if ($distinct && !in_array(Criteria::DISTINCT, $criteria->getSelectModifiers())) {
            $criteria->setDistinct();
        }

        if (!$criteria->hasSelectClause()) {
            GeoreferencedMapsPeer::addSelectColumns($criteria);
        }

        $criteria->clearOrderByColumns(); // ORDER BY won't ever affect the count
        $criteria->setDbName(GeoreferencedMapsPeer::DATABASE_NAME); // Set the correct dbName

        if ($con === null) {
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return GeoreferencedMaps
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doSelectOne(Criteria $criteria, PropelPDO $con = null)
    {
        $critcopy = clone $criteria;
        $critcopy->setLimit(1);
        $objects = GeoreferencedMapsPeer::doSelect($critcopy, $con);
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
        return GeoreferencedMapsPeer::populateObjects(GeoreferencedMapsPeer::doSelectStmt($criteria, $con));
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
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        if (!$criteria->hasSelectClause()) {
            $criteria = clone $criteria;
            GeoreferencedMapsPeer::addSelectColumns($criteria);
        }

        // Set the correct dbName
        $criteria->setDbName(GeoreferencedMapsPeer::DATABASE_NAME);

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
     * @param GeoreferencedMaps $obj A GeoreferencedMaps object.
     * @param      string $key (optional) key to use for instance map (for performance boost if key was already calculated externally).
     */
    public static function addInstanceToPool($obj, $key = null)
    {
        if (Propel::isInstancePoolingEnabled()) {
            if ($key === null) {
                $key = (string) $obj->getId();
            } // if key === null
            GeoreferencedMapsPeer::$instances[$key] = $obj;
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
     * @param      mixed $value A GeoreferencedMaps object or a primary key value.
     *
     * @return void
     * @throws PropelException - if the value is invalid.
     */
    public static function removeInstanceFromPool($value)
    {
        if (Propel::isInstancePoolingEnabled() && $value !== null) {
            if (is_object($value) && $value instanceof GeoreferencedMaps) {
                $key = (string) $value->getId();
            } elseif (is_scalar($value)) {
                // assume we've been passed a primary key
                $key = (string) $value;
            } else {
                $e = new PropelException("Invalid value passed to removeInstanceFromPool().  Expected primary key or GeoreferencedMaps object; got " . (is_object($value) ? get_class($value) . ' object.' : var_export($value,true)));
                throw $e;
            }

            unset(GeoreferencedMapsPeer::$instances[$key]);
        }
    } // removeInstanceFromPool()

    /**
     * Retrieves a string version of the primary key from the DB resultset row that can be used to uniquely identify a row in this table.
     *
     * For tables with a single-column primary key, that simple pkey value will be returned.  For tables with
     * a multi-column primary key, a serialize()d version of the primary key will be returned.
     *
     * @param      string $key The key (@see getPrimaryKeyHash()) for this instance.
     * @return GeoreferencedMaps Found object or null if 1) no instance exists for specified key or 2) instance pooling has been disabled.
     * @see        getPrimaryKeyHash()
     */
    public static function getInstanceFromPool($key)
    {
        if (Propel::isInstancePoolingEnabled()) {
            if (isset(GeoreferencedMapsPeer::$instances[$key])) {
                return GeoreferencedMapsPeer::$instances[$key];
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
        foreach (GeoreferencedMapsPeer::$instances as $instance) {
          $instance->clearAllReferences(true);
        }
      }
        GeoreferencedMapsPeer::$instances = array();
    }

    /**
     * Method to invalidate the instance pool of all tables related to georeferenced_maps
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
        $cls = GeoreferencedMapsPeer::getOMClass();
        // populate the object(s)
        while ($row = $stmt->fetch(PDO::FETCH_NUM)) {
            $key = GeoreferencedMapsPeer::getPrimaryKeyHashFromRow($row, 0);
            if (null !== ($obj = GeoreferencedMapsPeer::getInstanceFromPool($key))) {
                // We no longer rehydrate the object, since this can cause data loss.
                // See http://www.propelorm.org/ticket/509
                // $obj->hydrate($row, 0, true); // rehydrate
                $results[] = $obj;
            } else {
                $obj = new $cls();
                $obj->hydrate($row);
                $results[] = $obj;
                GeoreferencedMapsPeer::addInstanceToPool($obj, $key);
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
     * @return array (GeoreferencedMaps object, last column rank)
     */
    public static function populateObject($row, $startcol = 0)
    {
        $key = GeoreferencedMapsPeer::getPrimaryKeyHashFromRow($row, $startcol);
        if (null !== ($obj = GeoreferencedMapsPeer::getInstanceFromPool($key))) {
            // We no longer rehydrate the object, since this can cause data loss.
            // See http://www.propelorm.org/ticket/509
            // $obj->hydrate($row, $startcol, true); // rehydrate
            $col = $startcol + GeoreferencedMapsPeer::NUM_HYDRATE_COLUMNS;
        } else {
            $cls = GeoreferencedMapsPeer::OM_CLASS;
            $obj = new $cls();
            $col = $obj->hydrate($row, $startcol);
            GeoreferencedMapsPeer::addInstanceToPool($obj, $key);
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
        return Propel::getDatabaseMap(GeoreferencedMapsPeer::DATABASE_NAME)->getTable(GeoreferencedMapsPeer::TABLE_NAME);
    }

    /**
     * Add a TableMap instance to the database for this peer class.
     */
    public static function buildTableMap()
    {
      $dbMap = Propel::getDatabaseMap(BaseGeoreferencedMapsPeer::DATABASE_NAME);
      if (!$dbMap->hasTable(BaseGeoreferencedMapsPeer::TABLE_NAME)) {
        $dbMap->addTableObject(new \GeoreferencedMapsTableMap());
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
        return GeoreferencedMapsPeer::OM_CLASS;
    }

    /**
     * Performs an INSERT on the database, given a GeoreferencedMaps or Criteria object.
     *
     * @param      mixed $values Criteria or GeoreferencedMaps object containing data that is used to create the INSERT statement.
     * @param      PropelPDO $con the PropelPDO connection to use
     * @return mixed           The new primary key.
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doInsert($values, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        if ($values instanceof Criteria) {
            $criteria = clone $values; // rename for clarity
        } else {
            $criteria = $values->buildCriteria(); // build Criteria from GeoreferencedMaps object
        }

        if ($criteria->containsKey(GeoreferencedMapsPeer::ID) && $criteria->keyContainsValue(GeoreferencedMapsPeer::ID) ) {
            throw new PropelException('Cannot insert a value for auto-increment primary key ('.GeoreferencedMapsPeer::ID.')');
        }


        // Set the correct dbName
        $criteria->setDbName(GeoreferencedMapsPeer::DATABASE_NAME);

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
     * Performs an UPDATE on the database, given a GeoreferencedMaps or Criteria object.
     *
     * @param      mixed $values Criteria or GeoreferencedMaps object containing data that is used to create the UPDATE statement.
     * @param      PropelPDO $con The connection to use (specify PropelPDO connection object to exert more control over transactions).
     * @return int             The number of affected rows (if supported by underlying database driver).
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function doUpdate($values, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        $selectCriteria = new Criteria(GeoreferencedMapsPeer::DATABASE_NAME);

        if ($values instanceof Criteria) {
            $criteria = clone $values; // rename for clarity

            $comparison = $criteria->getComparison(GeoreferencedMapsPeer::ID);
            $value = $criteria->remove(GeoreferencedMapsPeer::ID);
            if ($value) {
                $selectCriteria->add(GeoreferencedMapsPeer::ID, $value, $comparison);
            } else {
                $selectCriteria->setPrimaryTableName(GeoreferencedMapsPeer::TABLE_NAME);
            }

        } else { // $values is GeoreferencedMaps object
            $criteria = $values->buildCriteria(); // gets full criteria
            $selectCriteria = $values->buildPkeyCriteria(); // gets criteria w/ primary key(s)
        }

        // set the correct dbName
        $criteria->setDbName(GeoreferencedMapsPeer::DATABASE_NAME);

        return BasePeer::doUpdate($selectCriteria, $criteria, $con);
    }

    /**
     * Deletes all rows from the georeferenced_maps table.
     *
     * @param      PropelPDO $con the connection to use
     * @return int             The number of affected rows (if supported by underlying database driver).
     * @throws PropelException
     */
    public static function doDeleteAll(PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }
        $affectedRows = 0; // initialize var to track total num of affected rows
        try {
            // use transaction because $criteria could contain info
            // for more than one table or we could emulating ON DELETE CASCADE, etc.
            $con->beginTransaction();
            $affectedRows += BasePeer::doDeleteAll(GeoreferencedMapsPeer::TABLE_NAME, $con, GeoreferencedMapsPeer::DATABASE_NAME);
            // Because this db requires some delete cascade/set null emulation, we have to
            // clear the cached instance *after* the emulation has happened (since
            // instances get re-added by the select statement contained therein).
            GeoreferencedMapsPeer::clearInstancePool();
            GeoreferencedMapsPeer::clearRelatedInstancePool();
            $con->commit();

            return $affectedRows;
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }
    }

    /**
     * Performs a DELETE on the database, given a GeoreferencedMaps or Criteria object OR a primary key value.
     *
     * @param      mixed $values Criteria or GeoreferencedMaps object or primary key or array of primary keys
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
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_WRITE);
        }

        if ($values instanceof Criteria) {
            // invalidate the cache for all objects of this type, since we have no
            // way of knowing (without running a query) what objects should be invalidated
            // from the cache based on this Criteria.
            GeoreferencedMapsPeer::clearInstancePool();
            // rename for clarity
            $criteria = clone $values;
        } elseif ($values instanceof GeoreferencedMaps) { // it's a model object
            // invalidate the cache for this single object
            GeoreferencedMapsPeer::removeInstanceFromPool($values);
            // create criteria based on pk values
            $criteria = $values->buildPkeyCriteria();
        } else { // it's a primary key, or an array of pks
            $criteria = new Criteria(GeoreferencedMapsPeer::DATABASE_NAME);
            $criteria->add(GeoreferencedMapsPeer::ID, (array) $values, Criteria::IN);
            // invalidate the cache for this object(s)
            foreach ((array) $values as $singleval) {
                GeoreferencedMapsPeer::removeInstanceFromPool($singleval);
            }
        }

        // Set the correct dbName
        $criteria->setDbName(GeoreferencedMapsPeer::DATABASE_NAME);

        $affectedRows = 0; // initialize var to track total num of affected rows

        try {
            // use transaction because $criteria could contain info
            // for more than one table or we could emulating ON DELETE CASCADE, etc.
            $con->beginTransaction();

            $affectedRows += BasePeer::doDelete($criteria, $con);
            GeoreferencedMapsPeer::clearRelatedInstancePool();
            $con->commit();

            return $affectedRows;
        } catch (Exception $e) {
            $con->rollBack();
            throw $e;
        }
    }

    /**
     * Validates all modified columns of given GeoreferencedMaps object.
     * If parameter $columns is either a single column name or an array of column names
     * than only those columns are validated.
     *
     * NOTICE: This does not apply to primary or foreign keys for now.
     *
     * @param GeoreferencedMaps $obj The object to validate.
     * @param      mixed $cols Column name or array of column names.
     *
     * @return mixed TRUE if all columns are valid or the error message of the first invalid column.
     */
    public static function doValidate($obj, $cols = null)
    {
        $columns = array();

        if ($cols) {
            $dbMap = Propel::getDatabaseMap(GeoreferencedMapsPeer::DATABASE_NAME);
            $tableMap = $dbMap->getTable(GeoreferencedMapsPeer::TABLE_NAME);

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

        return BasePeer::doValidate(GeoreferencedMapsPeer::DATABASE_NAME, GeoreferencedMapsPeer::TABLE_NAME, $columns);
    }

    /**
     * Retrieve a single object by pkey.
     *
     * @param string $pk the primary key.
     * @param      PropelPDO $con the connection to use
     * @return GeoreferencedMaps
     */
    public static function retrieveByPK($pk, PropelPDO $con = null)
    {

        if (null !== ($obj = GeoreferencedMapsPeer::getInstanceFromPool((string) $pk))) {
            return $obj;
        }

        if ($con === null) {
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        $criteria = new Criteria(GeoreferencedMapsPeer::DATABASE_NAME);
        $criteria->add(GeoreferencedMapsPeer::ID, $pk);

        $v = GeoreferencedMapsPeer::doSelect($criteria, $con);

        return !empty($v) > 0 ? $v[0] : null;
    }

    /**
     * Retrieve multiple objects by pkey.
     *
     * @param      array $pks List of primary keys
     * @param      PropelPDO $con the connection to use
     * @return GeoreferencedMaps[]
     * @throws PropelException Any exceptions caught during processing will be
     *		 rethrown wrapped into a PropelException.
     */
    public static function retrieveByPKs($pks, PropelPDO $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection(GeoreferencedMapsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }

        $objs = null;
        if (empty($pks)) {
            $objs = array();
        } else {
            $criteria = new Criteria(GeoreferencedMapsPeer::DATABASE_NAME);
            $criteria->add(GeoreferencedMapsPeer::ID, $pks, Criteria::IN);
            $objs = GeoreferencedMapsPeer::doSelect($criteria, $con);
        }

        return $objs;
    }

} // BaseGeoreferencedMapsPeer

// This is the static code needed to register the TableMap for this table with the main Propel class.
//
BaseGeoreferencedMapsPeer::buildTableMap();

