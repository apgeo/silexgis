<?php


/**
 * Base class that represents a query for the 'map_views' table.
 *
 *
 *
 * @method MapViewsQuery orderById($order = Criteria::ASC) Order by the id column
 * @method MapViewsQuery orderByName($order = Criteria::ASC) Order by the name column
 * @method MapViewsQuery orderByProperties($order = Criteria::ASC) Order by the properties column
 * @method MapViewsQuery orderByCenterGeometry($order = Criteria::ASC) Order by the center_geometry column
 * @method MapViewsQuery orderByIsDefault($order = Criteria::ASC) Order by the is_default column
 *
 * @method MapViewsQuery groupById() Group by the id column
 * @method MapViewsQuery groupByName() Group by the name column
 * @method MapViewsQuery groupByProperties() Group by the properties column
 * @method MapViewsQuery groupByCenterGeometry() Group by the center_geometry column
 * @method MapViewsQuery groupByIsDefault() Group by the is_default column
 *
 * @method MapViewsQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method MapViewsQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method MapViewsQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method MapViews findOne(PropelPDO $con = null) Return the first MapViews matching the query
 * @method MapViews findOneOrCreate(PropelPDO $con = null) Return the first MapViews matching the query, or a new MapViews object populated from the query conditions when no match is found
 *
 * @method MapViews findOneByName(string $name) Return the first MapViews filtered by the name column
 * @method MapViews findOneByProperties(string $properties) Return the first MapViews filtered by the properties column
 * @method MapViews findOneByCenterGeometry(string $center_geometry) Return the first MapViews filtered by the center_geometry column
 * @method MapViews findOneByIsDefault(string $is_default) Return the first MapViews filtered by the is_default column
 *
 * @method array findById(string $id) Return MapViews objects filtered by the id column
 * @method array findByName(string $name) Return MapViews objects filtered by the name column
 * @method array findByProperties(string $properties) Return MapViews objects filtered by the properties column
 * @method array findByCenterGeometry(string $center_geometry) Return MapViews objects filtered by the center_geometry column
 * @method array findByIsDefault(string $is_default) Return MapViews objects filtered by the is_default column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseMapViewsQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseMapViewsQuery object.
     *
     * @param     string $dbName The dabase name
     * @param     string $modelName The phpName of a model, e.g. 'Book'
     * @param     string $modelAlias The alias for the model in this query, e.g. 'b'
     */
    public function __construct($dbName = null, $modelName = null, $modelAlias = null)
    {
        if (null === $dbName) {
            $dbName = 'speogis';
        }
        if (null === $modelName) {
            $modelName = 'MapViews';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new MapViewsQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   MapViewsQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return MapViewsQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof MapViewsQuery) {
            return $criteria;
        }
        $query = new MapViewsQuery(null, null, $modelAlias);

        if ($criteria instanceof Criteria) {
            $query->mergeWith($criteria);
        }

        return $query;
    }

    /**
     * Find object by primary key.
     * Propel uses the instance pool to skip the database if the object exists.
     * Go fast if the query is untouched.
     *
     * <code>
     * $obj  = $c->findPk(12, $con);
     * </code>
     *
     * @param mixed $key Primary key to use for the query
     * @param     PropelPDO $con an optional connection object
     *
     * @return   MapViews|MapViews[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = MapViewsPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(MapViewsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
        }
        $this->basePreSelect($con);
        if ($this->formatter || $this->modelAlias || $this->with || $this->select
         || $this->selectColumns || $this->asColumns || $this->selectModifiers
         || $this->map || $this->having || $this->joins) {
            return $this->findPkComplex($key, $con);
        } else {
            return $this->findPkSimple($key, $con);
        }
    }

    /**
     * Alias of findPk to use instance pooling
     *
     * @param     mixed $key Primary key to use for the query
     * @param     PropelPDO $con A connection object
     *
     * @return                 MapViews A model object, or null if the key is not found
     * @throws PropelException
     */
     public function findOneById($key, $con = null)
     {
        return $this->findPk($key, $con);
     }

    /**
     * Find object by primary key using raw SQL to go fast.
     * Bypass doSelect() and the object formatter by using generated code.
     *
     * @param     mixed $key Primary key to use for the query
     * @param     PropelPDO $con A connection object
     *
     * @return                 MapViews A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `name`, `properties`, `center_geometry`, `is_default` FROM `map_views` WHERE `id` = :p0';
        try {
            $stmt = $con->prepare($sql);
            $stmt->bindValue(':p0', $key, PDO::PARAM_STR);
            $stmt->execute();
        } catch (Exception $e) {
            Propel::log($e->getMessage(), Propel::LOG_ERR);
            throw new PropelException(sprintf('Unable to execute SELECT statement [%s]', $sql), $e);
        }
        $obj = null;
        if ($row = $stmt->fetch(PDO::FETCH_NUM)) {
            $obj = new MapViews();
            $obj->hydrate($row);
            MapViewsPeer::addInstanceToPool($obj, (string) $key);
        }
        $stmt->closeCursor();

        return $obj;
    }

    /**
     * Find object by primary key.
     *
     * @param     mixed $key Primary key to use for the query
     * @param     PropelPDO $con A connection object
     *
     * @return MapViews|MapViews[]|mixed the result, formatted by the current formatter
     */
    protected function findPkComplex($key, $con)
    {
        // As the query uses a PK condition, no limit(1) is necessary.
        $criteria = $this->isKeepQuery() ? clone $this : $this;
        $stmt = $criteria
            ->filterByPrimaryKey($key)
            ->doSelect($con);

        return $criteria->getFormatter()->init($criteria)->formatOne($stmt);
    }

    /**
     * Find objects by primary key
     * <code>
     * $objs = $c->findPks(array(12, 56, 832), $con);
     * </code>
     * @param     array $keys Primary keys to use for the query
     * @param     PropelPDO $con an optional connection object
     *
     * @return PropelObjectCollection|MapViews[]|mixed the list of results, formatted by the current formatter
     */
    public function findPks($keys, $con = null)
    {
        if ($con === null) {
            $con = Propel::getConnection($this->getDbName(), Propel::CONNECTION_READ);
        }
        $this->basePreSelect($con);
        $criteria = $this->isKeepQuery() ? clone $this : $this;
        $stmt = $criteria
            ->filterByPrimaryKeys($keys)
            ->doSelect($con);

        return $criteria->getFormatter()->init($criteria)->format($stmt);
    }

    /**
     * Filter the query by primary key
     *
     * @param     mixed $key Primary key to use for the query
     *
     * @return MapViewsQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(MapViewsPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return MapViewsQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(MapViewsPeer::ID, $keys, Criteria::IN);
    }

    /**
     * Filter the query on the id column
     *
     * Example usage:
     * <code>
     * $query->filterById(1234); // WHERE id = 1234
     * $query->filterById(array(12, 34)); // WHERE id IN (12, 34)
     * $query->filterById(array('min' => 12)); // WHERE id >= 12
     * $query->filterById(array('max' => 12)); // WHERE id <= 12
     * </code>
     *
     * @param     mixed $id The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return MapViewsQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(MapViewsPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(MapViewsPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(MapViewsPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the name column
     *
     * Example usage:
     * <code>
     * $query->filterByName('fooValue');   // WHERE name = 'fooValue'
     * $query->filterByName('%fooValue%'); // WHERE name LIKE '%fooValue%'
     * </code>
     *
     * @param     string $name The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return MapViewsQuery The current query, for fluid interface
     */
    public function filterByName($name = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($name)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $name)) {
                $name = str_replace('*', '%', $name);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(MapViewsPeer::NAME, $name, $comparison);
    }

    /**
     * Filter the query on the properties column
     *
     * Example usage:
     * <code>
     * $query->filterByProperties('fooValue');   // WHERE properties = 'fooValue'
     * $query->filterByProperties('%fooValue%'); // WHERE properties LIKE '%fooValue%'
     * </code>
     *
     * @param     string $properties The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return MapViewsQuery The current query, for fluid interface
     */
    public function filterByProperties($properties = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($properties)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $properties)) {
                $properties = str_replace('*', '%', $properties);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(MapViewsPeer::PROPERTIES, $properties, $comparison);
    }

    /**
     * Filter the query on the center_geometry column
     *
     * Example usage:
     * <code>
     * $query->filterByCenterGeometry('fooValue');   // WHERE center_geometry = 'fooValue'
     * $query->filterByCenterGeometry('%fooValue%'); // WHERE center_geometry LIKE '%fooValue%'
     * </code>
     *
     * @param     string $centerGeometry The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return MapViewsQuery The current query, for fluid interface
     */
    public function filterByCenterGeometry($centerGeometry = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($centerGeometry)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $centerGeometry)) {
                $centerGeometry = str_replace('*', '%', $centerGeometry);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(MapViewsPeer::CENTER_GEOMETRY, $centerGeometry, $comparison);
    }

    /**
     * Filter the query on the is_default column
     *
     * Example usage:
     * <code>
     * $query->filterByIsDefault('fooValue');   // WHERE is_default = 'fooValue'
     * $query->filterByIsDefault('%fooValue%'); // WHERE is_default LIKE '%fooValue%'
     * </code>
     *
     * @param     string $isDefault The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return MapViewsQuery The current query, for fluid interface
     */
    public function filterByIsDefault($isDefault = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($isDefault)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $isDefault)) {
                $isDefault = str_replace('*', '%', $isDefault);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(MapViewsPeer::IS_DEFAULT, $isDefault, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   MapViews $mapViews Object to remove from the list of results
     *
     * @return MapViewsQuery The current query, for fluid interface
     */
    public function prune($mapViews = null)
    {
        if ($mapViews) {
            $this->addUsingAlias(MapViewsPeer::ID, $mapViews->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
