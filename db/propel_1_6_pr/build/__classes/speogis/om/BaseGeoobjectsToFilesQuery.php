<?php


/**
 * Base class that represents a query for the 'geoobjects_to_files' table.
 *
 *
 *
 * @method GeoobjectsToFilesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method GeoobjectsToFilesQuery orderByFileId($order = Criteria::ASC) Order by the file_id column
 * @method GeoobjectsToFilesQuery orderByGeoobjectId($order = Criteria::ASC) Order by the geoobject_id column
 * @method GeoobjectsToFilesQuery orderByGeoobjectType($order = Criteria::ASC) Order by the geoobject_type column
 *
 * @method GeoobjectsToFilesQuery groupById() Group by the id column
 * @method GeoobjectsToFilesQuery groupByFileId() Group by the file_id column
 * @method GeoobjectsToFilesQuery groupByGeoobjectId() Group by the geoobject_id column
 * @method GeoobjectsToFilesQuery groupByGeoobjectType() Group by the geoobject_type column
 *
 * @method GeoobjectsToFilesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method GeoobjectsToFilesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method GeoobjectsToFilesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method GeoobjectsToFiles findOne(PropelPDO $con = null) Return the first GeoobjectsToFiles matching the query
 * @method GeoobjectsToFiles findOneOrCreate(PropelPDO $con = null) Return the first GeoobjectsToFiles matching the query, or a new GeoobjectsToFiles object populated from the query conditions when no match is found
 *
 * @method GeoobjectsToFiles findOneByFileId(string $file_id) Return the first GeoobjectsToFiles filtered by the file_id column
 * @method GeoobjectsToFiles findOneByGeoobjectId(string $geoobject_id) Return the first GeoobjectsToFiles filtered by the geoobject_id column
 * @method GeoobjectsToFiles findOneByGeoobjectType(string $geoobject_type) Return the first GeoobjectsToFiles filtered by the geoobject_type column
 *
 * @method array findById(string $id) Return GeoobjectsToFiles objects filtered by the id column
 * @method array findByFileId(string $file_id) Return GeoobjectsToFiles objects filtered by the file_id column
 * @method array findByGeoobjectId(string $geoobject_id) Return GeoobjectsToFiles objects filtered by the geoobject_id column
 * @method array findByGeoobjectType(string $geoobject_type) Return GeoobjectsToFiles objects filtered by the geoobject_type column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseGeoobjectsToFilesQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseGeoobjectsToFilesQuery object.
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
            $modelName = 'GeoobjectsToFiles';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new GeoobjectsToFilesQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   GeoobjectsToFilesQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return GeoobjectsToFilesQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof GeoobjectsToFilesQuery) {
            return $criteria;
        }
        $query = new GeoobjectsToFilesQuery(null, null, $modelAlias);

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
     * @return   GeoobjectsToFiles|GeoobjectsToFiles[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = GeoobjectsToFilesPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(GeoobjectsToFilesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 GeoobjectsToFiles A model object, or null if the key is not found
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
     * @return                 GeoobjectsToFiles A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `file_id`, `geoobject_id`, `geoobject_type` FROM `geoobjects_to_files` WHERE `id` = :p0';
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
            $obj = new GeoobjectsToFiles();
            $obj->hydrate($row);
            GeoobjectsToFilesPeer::addInstanceToPool($obj, (string) $key);
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
     * @return GeoobjectsToFiles|GeoobjectsToFiles[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|GeoobjectsToFiles[]|mixed the list of results, formatted by the current formatter
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
     * @return GeoobjectsToFilesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(GeoobjectsToFilesPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return GeoobjectsToFilesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(GeoobjectsToFilesPeer::ID, $keys, Criteria::IN);
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
     * @return GeoobjectsToFilesQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(GeoobjectsToFilesPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(GeoobjectsToFilesPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoobjectsToFilesPeer::ID, $id, $comparison);
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
     * @return GeoobjectsToFilesQuery The current query, for fluid interface
     */
    public function filterByFileId($fileId = null, $comparison = null)
    {
        if (is_array($fileId)) {
            $useMinMax = false;
            if (isset($fileId['min'])) {
                $this->addUsingAlias(GeoobjectsToFilesPeer::FILE_ID, $fileId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($fileId['max'])) {
                $this->addUsingAlias(GeoobjectsToFilesPeer::FILE_ID, $fileId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoobjectsToFilesPeer::FILE_ID, $fileId, $comparison);
    }

    /**
     * Filter the query on the geoobject_id column
     *
     * Example usage:
     * <code>
     * $query->filterByGeoobjectId(1234); // WHERE geoobject_id = 1234
     * $query->filterByGeoobjectId(array(12, 34)); // WHERE geoobject_id IN (12, 34)
     * $query->filterByGeoobjectId(array('min' => 12)); // WHERE geoobject_id >= 12
     * $query->filterByGeoobjectId(array('max' => 12)); // WHERE geoobject_id <= 12
     * </code>
     *
     * @param     mixed $geoobjectId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoobjectsToFilesQuery The current query, for fluid interface
     */
    public function filterByGeoobjectId($geoobjectId = null, $comparison = null)
    {
        if (is_array($geoobjectId)) {
            $useMinMax = false;
            if (isset($geoobjectId['min'])) {
                $this->addUsingAlias(GeoobjectsToFilesPeer::GEOOBJECT_ID, $geoobjectId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($geoobjectId['max'])) {
                $this->addUsingAlias(GeoobjectsToFilesPeer::GEOOBJECT_ID, $geoobjectId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(GeoobjectsToFilesPeer::GEOOBJECT_ID, $geoobjectId, $comparison);
    }

    /**
     * Filter the query on the geoobject_type column
     *
     * Example usage:
     * <code>
     * $query->filterByGeoobjectType('fooValue');   // WHERE geoobject_type = 'fooValue'
     * $query->filterByGeoobjectType('%fooValue%'); // WHERE geoobject_type LIKE '%fooValue%'
     * </code>
     *
     * @param     string $geoobjectType The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return GeoobjectsToFilesQuery The current query, for fluid interface
     */
    public function filterByGeoobjectType($geoobjectType = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($geoobjectType)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $geoobjectType)) {
                $geoobjectType = str_replace('*', '%', $geoobjectType);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(GeoobjectsToFilesPeer::GEOOBJECT_TYPE, $geoobjectType, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   GeoobjectsToFiles $geoobjectsToFiles Object to remove from the list of results
     *
     * @return GeoobjectsToFilesQuery The current query, for fluid interface
     */
    public function prune($geoobjectsToFiles = null)
    {
        if ($geoobjectsToFiles) {
            $this->addUsingAlias(GeoobjectsToFilesPeer::ID, $geoobjectsToFiles->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
