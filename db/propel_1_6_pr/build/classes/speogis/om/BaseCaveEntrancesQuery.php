<?php


/**
 * Base class that represents a query for the 'cave_entrances' table.
 *
 *
 *
 * @method CaveEntrancesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method CaveEntrancesQuery orderByName($order = Criteria::ASC) Order by the name column
 * @method CaveEntrancesQuery orderByPointId($order = Criteria::ASC) Order by the point_id column
 * @method CaveEntrancesQuery orderByEntrancetype($order = Criteria::ASC) Order by the entranceType column
 * @method CaveEntrancesQuery orderByDescription($order = Criteria::ASC) Order by the description column
 * @method CaveEntrancesQuery orderByIsMainEntrance($order = Criteria::ASC) Order by the is_main_entrance column
 * @method CaveEntrancesQuery orderByHydrologicType($order = Criteria::ASC) Order by the hydrologic_type column
 * @method CaveEntrancesQuery orderByCaveId($order = Criteria::ASC) Order by the cave_id column
 *
 * @method CaveEntrancesQuery groupById() Group by the id column
 * @method CaveEntrancesQuery groupByName() Group by the name column
 * @method CaveEntrancesQuery groupByPointId() Group by the point_id column
 * @method CaveEntrancesQuery groupByEntrancetype() Group by the entranceType column
 * @method CaveEntrancesQuery groupByDescription() Group by the description column
 * @method CaveEntrancesQuery groupByIsMainEntrance() Group by the is_main_entrance column
 * @method CaveEntrancesQuery groupByHydrologicType() Group by the hydrologic_type column
 * @method CaveEntrancesQuery groupByCaveId() Group by the cave_id column
 *
 * @method CaveEntrancesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method CaveEntrancesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method CaveEntrancesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method CaveEntrances findOne(PropelPDO $con = null) Return the first CaveEntrances matching the query
 * @method CaveEntrances findOneOrCreate(PropelPDO $con = null) Return the first CaveEntrances matching the query, or a new CaveEntrances object populated from the query conditions when no match is found
 *
 * @method CaveEntrances findOneByName(string $name) Return the first CaveEntrances filtered by the name column
 * @method CaveEntrances findOneByPointId(string $point_id) Return the first CaveEntrances filtered by the point_id column
 * @method CaveEntrances findOneByEntrancetype(string $entranceType) Return the first CaveEntrances filtered by the entranceType column
 * @method CaveEntrances findOneByDescription(string $description) Return the first CaveEntrances filtered by the description column
 * @method CaveEntrances findOneByIsMainEntrance(boolean $is_main_entrance) Return the first CaveEntrances filtered by the is_main_entrance column
 * @method CaveEntrances findOneByHydrologicType(string $hydrologic_type) Return the first CaveEntrances filtered by the hydrologic_type column
 * @method CaveEntrances findOneByCaveId(string $cave_id) Return the first CaveEntrances filtered by the cave_id column
 *
 * @method array findById(string $id) Return CaveEntrances objects filtered by the id column
 * @method array findByName(string $name) Return CaveEntrances objects filtered by the name column
 * @method array findByPointId(string $point_id) Return CaveEntrances objects filtered by the point_id column
 * @method array findByEntrancetype(string $entranceType) Return CaveEntrances objects filtered by the entranceType column
 * @method array findByDescription(string $description) Return CaveEntrances objects filtered by the description column
 * @method array findByIsMainEntrance(boolean $is_main_entrance) Return CaveEntrances objects filtered by the is_main_entrance column
 * @method array findByHydrologicType(string $hydrologic_type) Return CaveEntrances objects filtered by the hydrologic_type column
 * @method array findByCaveId(string $cave_id) Return CaveEntrances objects filtered by the cave_id column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseCaveEntrancesQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseCaveEntrancesQuery object.
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
            $modelName = 'CaveEntrances';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new CaveEntrancesQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   CaveEntrancesQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return CaveEntrancesQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof CaveEntrancesQuery) {
            return $criteria;
        }
        $query = new CaveEntrancesQuery(null, null, $modelAlias);

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
     * @return   CaveEntrances|CaveEntrances[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = CaveEntrancesPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(CaveEntrancesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 CaveEntrances A model object, or null if the key is not found
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
     * @return                 CaveEntrances A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `name`, `point_id`, `entranceType`, `description`, `is_main_entrance`, `hydrologic_type`, `cave_id` FROM `cave_entrances` WHERE `id` = :p0';
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
            $obj = new CaveEntrances();
            $obj->hydrate($row);
            CaveEntrancesPeer::addInstanceToPool($obj, (string) $key);
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
     * @return CaveEntrances|CaveEntrances[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|CaveEntrances[]|mixed the list of results, formatted by the current formatter
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
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(CaveEntrancesPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(CaveEntrancesPeer::ID, $keys, Criteria::IN);
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
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(CaveEntrancesPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(CaveEntrancesPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CaveEntrancesPeer::ID, $id, $comparison);
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
     * @return CaveEntrancesQuery The current query, for fluid interface
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

        return $this->addUsingAlias(CaveEntrancesPeer::NAME, $name, $comparison);
    }

    /**
     * Filter the query on the point_id column
     *
     * Example usage:
     * <code>
     * $query->filterByPointId(1234); // WHERE point_id = 1234
     * $query->filterByPointId(array(12, 34)); // WHERE point_id IN (12, 34)
     * $query->filterByPointId(array('min' => 12)); // WHERE point_id >= 12
     * $query->filterByPointId(array('max' => 12)); // WHERE point_id <= 12
     * </code>
     *
     * @param     mixed $pointId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterByPointId($pointId = null, $comparison = null)
    {
        if (is_array($pointId)) {
            $useMinMax = false;
            if (isset($pointId['min'])) {
                $this->addUsingAlias(CaveEntrancesPeer::POINT_ID, $pointId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($pointId['max'])) {
                $this->addUsingAlias(CaveEntrancesPeer::POINT_ID, $pointId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CaveEntrancesPeer::POINT_ID, $pointId, $comparison);
    }

    /**
     * Filter the query on the entranceType column
     *
     * Example usage:
     * <code>
     * $query->filterByEntrancetype(1234); // WHERE entranceType = 1234
     * $query->filterByEntrancetype(array(12, 34)); // WHERE entranceType IN (12, 34)
     * $query->filterByEntrancetype(array('min' => 12)); // WHERE entranceType >= 12
     * $query->filterByEntrancetype(array('max' => 12)); // WHERE entranceType <= 12
     * </code>
     *
     * @param     mixed $entrancetype The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterByEntrancetype($entrancetype = null, $comparison = null)
    {
        if (is_array($entrancetype)) {
            $useMinMax = false;
            if (isset($entrancetype['min'])) {
                $this->addUsingAlias(CaveEntrancesPeer::ENTRANCETYPE, $entrancetype['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($entrancetype['max'])) {
                $this->addUsingAlias(CaveEntrancesPeer::ENTRANCETYPE, $entrancetype['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CaveEntrancesPeer::ENTRANCETYPE, $entrancetype, $comparison);
    }

    /**
     * Filter the query on the description column
     *
     * Example usage:
     * <code>
     * $query->filterByDescription('fooValue');   // WHERE description = 'fooValue'
     * $query->filterByDescription('%fooValue%'); // WHERE description LIKE '%fooValue%'
     * </code>
     *
     * @param     string $description The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterByDescription($description = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($description)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $description)) {
                $description = str_replace('*', '%', $description);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(CaveEntrancesPeer::DESCRIPTION, $description, $comparison);
    }

    /**
     * Filter the query on the is_main_entrance column
     *
     * Example usage:
     * <code>
     * $query->filterByIsMainEntrance(true); // WHERE is_main_entrance = true
     * $query->filterByIsMainEntrance('yes'); // WHERE is_main_entrance = true
     * </code>
     *
     * @param     boolean|string $isMainEntrance The value to use as filter.
     *              Non-boolean arguments are converted using the following rules:
     *                * 1, '1', 'true',  'on',  and 'yes' are converted to boolean true
     *                * 0, '0', 'false', 'off', and 'no'  are converted to boolean false
     *              Check on string values is case insensitive (so 'FaLsE' is seen as 'false').
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterByIsMainEntrance($isMainEntrance = null, $comparison = null)
    {
        if (is_string($isMainEntrance)) {
            $isMainEntrance = in_array(strtolower($isMainEntrance), array('false', 'off', '-', 'no', 'n', '0', '')) ? false : true;
        }

        return $this->addUsingAlias(CaveEntrancesPeer::IS_MAIN_ENTRANCE, $isMainEntrance, $comparison);
    }

    /**
     * Filter the query on the hydrologic_type column
     *
     * Example usage:
     * <code>
     * $query->filterByHydrologicType(1234); // WHERE hydrologic_type = 1234
     * $query->filterByHydrologicType(array(12, 34)); // WHERE hydrologic_type IN (12, 34)
     * $query->filterByHydrologicType(array('min' => 12)); // WHERE hydrologic_type >= 12
     * $query->filterByHydrologicType(array('max' => 12)); // WHERE hydrologic_type <= 12
     * </code>
     *
     * @param     mixed $hydrologicType The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterByHydrologicType($hydrologicType = null, $comparison = null)
    {
        if (is_array($hydrologicType)) {
            $useMinMax = false;
            if (isset($hydrologicType['min'])) {
                $this->addUsingAlias(CaveEntrancesPeer::HYDROLOGIC_TYPE, $hydrologicType['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($hydrologicType['max'])) {
                $this->addUsingAlias(CaveEntrancesPeer::HYDROLOGIC_TYPE, $hydrologicType['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CaveEntrancesPeer::HYDROLOGIC_TYPE, $hydrologicType, $comparison);
    }

    /**
     * Filter the query on the cave_id column
     *
     * Example usage:
     * <code>
     * $query->filterByCaveId(1234); // WHERE cave_id = 1234
     * $query->filterByCaveId(array(12, 34)); // WHERE cave_id IN (12, 34)
     * $query->filterByCaveId(array('min' => 12)); // WHERE cave_id >= 12
     * $query->filterByCaveId(array('max' => 12)); // WHERE cave_id <= 12
     * </code>
     *
     * @param     mixed $caveId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function filterByCaveId($caveId = null, $comparison = null)
    {
        if (is_array($caveId)) {
            $useMinMax = false;
            if (isset($caveId['min'])) {
                $this->addUsingAlias(CaveEntrancesPeer::CAVE_ID, $caveId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($caveId['max'])) {
                $this->addUsingAlias(CaveEntrancesPeer::CAVE_ID, $caveId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(CaveEntrancesPeer::CAVE_ID, $caveId, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   CaveEntrances $caveEntrances Object to remove from the list of results
     *
     * @return CaveEntrancesQuery The current query, for fluid interface
     */
    public function prune($caveEntrances = null)
    {
        if ($caveEntrances) {
            $this->addUsingAlias(CaveEntrancesPeer::ID, $caveEntrances->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
