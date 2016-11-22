<?php


/**
 * Base class that represents a query for the 'features' table.
 *
 *
 *
 * @method FeaturesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method FeaturesQuery orderByName($order = Criteria::ASC) Order by the name column
 * @method FeaturesQuery orderByPointId($order = Criteria::ASC) Order by the point_id column
 * @method FeaturesQuery orderByDescription($order = Criteria::ASC) Order by the description column
 * @method FeaturesQuery orderByFeatureTypeId($order = Criteria::ASC) Order by the feature_type_id column
 *
 * @method FeaturesQuery groupById() Group by the id column
 * @method FeaturesQuery groupByName() Group by the name column
 * @method FeaturesQuery groupByPointId() Group by the point_id column
 * @method FeaturesQuery groupByDescription() Group by the description column
 * @method FeaturesQuery groupByFeatureTypeId() Group by the feature_type_id column
 *
 * @method FeaturesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method FeaturesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method FeaturesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method Features findOne(PropelPDO $con = null) Return the first Features matching the query
 * @method Features findOneOrCreate(PropelPDO $con = null) Return the first Features matching the query, or a new Features object populated from the query conditions when no match is found
 *
 * @method Features findOneByName(string $name) Return the first Features filtered by the name column
 * @method Features findOneByPointId(string $point_id) Return the first Features filtered by the point_id column
 * @method Features findOneByDescription(string $description) Return the first Features filtered by the description column
 * @method Features findOneByFeatureTypeId(string $feature_type_id) Return the first Features filtered by the feature_type_id column
 *
 * @method array findById(string $id) Return Features objects filtered by the id column
 * @method array findByName(string $name) Return Features objects filtered by the name column
 * @method array findByPointId(string $point_id) Return Features objects filtered by the point_id column
 * @method array findByDescription(string $description) Return Features objects filtered by the description column
 * @method array findByFeatureTypeId(string $feature_type_id) Return Features objects filtered by the feature_type_id column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseFeaturesQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseFeaturesQuery object.
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
            $modelName = 'Features';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new FeaturesQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   FeaturesQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return FeaturesQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof FeaturesQuery) {
            return $criteria;
        }
        $query = new FeaturesQuery(null, null, $modelAlias);

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
     * @return   Features|Features[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = FeaturesPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(FeaturesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 Features A model object, or null if the key is not found
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
     * @return                 Features A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `name`, `point_id`, `description`, `feature_type_id` FROM `features` WHERE `id` = :p0';
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
            $obj = new Features();
            $obj->hydrate($row);
            FeaturesPeer::addInstanceToPool($obj, (string) $key);
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
     * @return Features|Features[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|Features[]|mixed the list of results, formatted by the current formatter
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
     * @return FeaturesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(FeaturesPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return FeaturesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(FeaturesPeer::ID, $keys, Criteria::IN);
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
     * @return FeaturesQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(FeaturesPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(FeaturesPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(FeaturesPeer::ID, $id, $comparison);
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
     * @return FeaturesQuery The current query, for fluid interface
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

        return $this->addUsingAlias(FeaturesPeer::NAME, $name, $comparison);
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
     * @return FeaturesQuery The current query, for fluid interface
     */
    public function filterByPointId($pointId = null, $comparison = null)
    {
        if (is_array($pointId)) {
            $useMinMax = false;
            if (isset($pointId['min'])) {
                $this->addUsingAlias(FeaturesPeer::POINT_ID, $pointId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($pointId['max'])) {
                $this->addUsingAlias(FeaturesPeer::POINT_ID, $pointId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(FeaturesPeer::POINT_ID, $pointId, $comparison);
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
     * @return FeaturesQuery The current query, for fluid interface
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

        return $this->addUsingAlias(FeaturesPeer::DESCRIPTION, $description, $comparison);
    }

    /**
     * Filter the query on the feature_type_id column
     *
     * Example usage:
     * <code>
     * $query->filterByFeatureTypeId(1234); // WHERE feature_type_id = 1234
     * $query->filterByFeatureTypeId(array(12, 34)); // WHERE feature_type_id IN (12, 34)
     * $query->filterByFeatureTypeId(array('min' => 12)); // WHERE feature_type_id >= 12
     * $query->filterByFeatureTypeId(array('max' => 12)); // WHERE feature_type_id <= 12
     * </code>
     *
     * @param     mixed $featureTypeId The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return FeaturesQuery The current query, for fluid interface
     */
    public function filterByFeatureTypeId($featureTypeId = null, $comparison = null)
    {
        if (is_array($featureTypeId)) {
            $useMinMax = false;
            if (isset($featureTypeId['min'])) {
                $this->addUsingAlias(FeaturesPeer::FEATURE_TYPE_ID, $featureTypeId['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($featureTypeId['max'])) {
                $this->addUsingAlias(FeaturesPeer::FEATURE_TYPE_ID, $featureTypeId['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(FeaturesPeer::FEATURE_TYPE_ID, $featureTypeId, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   Features $features Object to remove from the list of results
     *
     * @return FeaturesQuery The current query, for fluid interface
     */
    public function prune($features = null)
    {
        if ($features) {
            $this->addUsingAlias(FeaturesPeer::ID, $features->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
