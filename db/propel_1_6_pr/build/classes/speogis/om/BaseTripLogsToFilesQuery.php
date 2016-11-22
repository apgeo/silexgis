<?php


/**
 * Base class that represents a query for the 'trip_logs_to_files' table.
 *
 *
 *
 * @method TripLogsToFilesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method TripLogsToFilesQuery orderByFileId($order = Criteria::ASC) Order by the file_id column
 * @method TripLogsToFilesQuery orderByTripLogId($order = Criteria::ASC) Order by the trip_log_id column
 *
 * @method TripLogsToFilesQuery groupById() Group by the id column
 * @method TripLogsToFilesQuery groupByFileId() Group by the file_id column
 * @method TripLogsToFilesQuery groupByTripLogId() Group by the trip_log_id column
 *
 * @method TripLogsToFilesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method TripLogsToFilesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method TripLogsToFilesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method TripLogsToFiles findOne(PropelPDO $con = null) Return the first TripLogsToFiles matching the query
 * @method TripLogsToFiles findOneOrCreate(PropelPDO $con = null) Return the first TripLogsToFiles matching the query, or a new TripLogsToFiles object populated from the query conditions when no match is found
 *
 * @method TripLogsToFiles findOneByFileId(string $file_id) Return the first TripLogsToFiles filtered by the file_id column
 * @method TripLogsToFiles findOneByTripLogId(string $trip_log_id) Return the first TripLogsToFiles filtered by the trip_log_id column
 *
 * @method array findById(string $id) Return TripLogsToFiles objects filtered by the id column
 * @method array findByFileId(string $file_id) Return TripLogsToFiles objects filtered by the file_id column
 * @method array findByTripLogId(string $trip_log_id) Return TripLogsToFiles objects filtered by the trip_log_id column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseTripLogsToFilesQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseTripLogsToFilesQuery object.
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
            $modelName = 'TripLogsToFiles';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new TripLogsToFilesQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   TripLogsToFilesQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return TripLogsToFilesQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof TripLogsToFilesQuery) {
            return $criteria;
        }
        $query = new TripLogsToFilesQuery(null, null, $modelAlias);

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
     * @return   TripLogsToFiles|TripLogsToFiles[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = TripLogsToFilesPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(TripLogsToFilesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 TripLogsToFiles A model object, or null if the key is not found
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
     * @return                 TripLogsToFiles A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `file_id`, `trip_log_id` FROM `trip_logs_to_files` WHERE `id` = :p0';
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
            $obj = new TripLogsToFiles();
            $obj->hydrate($row);
            TripLogsToFilesPeer::addInstanceToPool($obj, (string) $key);
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
     * @return TripLogsToFiles|TripLogsToFiles[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|TripLogsToFiles[]|mixed the list of results, formatted by the current formatter
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
     * @return TripLogsToFilesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(TripLogsToFilesPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return TripLogsToFilesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(TripLogsToFilesPeer::ID, $keys, Criteria::IN);
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
     * @return TripLogsToFilesQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(TripLogsToFilesPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(TripLogsToFilesPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsToFilesPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the file_id column
     *
     * Example usage:
     * <code>
     * $query->filterByFileId(1234); // WHERE file_id = 1234
     * $query->filterByFileId(array(12, 34)); // WHERE file_id IN (12, 34)
     * $query->filterByFileId(array('min' => 12)); // WHERE file_id >= 12
     * $query->filterByFileId(array('max' => 12)); // WHERE file_id <= 12
     * </code>
     *
     * @param     mixed $fileId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsToFilesQuery The current query, for fluid interface
     */
    public function filterByFileId($fileId = null, $comparison = null)
    {
        if (is_array($fileId)) {
            $useMinMax = false;
            if (isset($fileId['min'])) {
                $this->addUsingAlias(TripLogsToFilesPeer::FILE_ID, $fileId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($fileId['max'])) {
                $this->addUsingAlias(TripLogsToFilesPeer::FILE_ID, $fileId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsToFilesPeer::FILE_ID, $fileId, $comparison);
    }

    /**
     * Filter the query on the trip_log_id column
     *
     * Example usage:
     * <code>
     * $query->filterByTripLogId(1234); // WHERE trip_log_id = 1234
     * $query->filterByTripLogId(array(12, 34)); // WHERE trip_log_id IN (12, 34)
     * $query->filterByTripLogId(array('min' => 12)); // WHERE trip_log_id >= 12
     * $query->filterByTripLogId(array('max' => 12)); // WHERE trip_log_id <= 12
     * </code>
     *
     * @param     mixed $tripLogId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TripLogsToFilesQuery The current query, for fluid interface
     */
    public function filterByTripLogId($tripLogId = null, $comparison = null)
    {
        if (is_array($tripLogId)) {
            $useMinMax = false;
            if (isset($tripLogId['min'])) {
                $this->addUsingAlias(TripLogsToFilesPeer::TRIP_LOG_ID, $tripLogId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($tripLogId['max'])) {
                $this->addUsingAlias(TripLogsToFilesPeer::TRIP_LOG_ID, $tripLogId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TripLogsToFilesPeer::TRIP_LOG_ID, $tripLogId, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   TripLogsToFiles $tripLogsToFiles Object to remove from the list of results
     *
     * @return TripLogsToFilesQuery The current query, for fluid interface
     */
    public function prune($tripLogsToFiles = null)
    {
        if ($tripLogsToFiles) {
            $this->addUsingAlias(TripLogsToFilesPeer::ID, $tripLogsToFiles->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
