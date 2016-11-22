<?php


/**
 * Base class that represents a query for the 'tags' table.
 *
 *
 *
 * @method TagsQuery orderById($order = Criteria::ASC) Order by the id column
 * @method TagsQuery orderByType($order = Criteria::ASC) Order by the type column
 * @method TagsQuery orderByK($order = Criteria::ASC) Order by the k column
 * @method TagsQuery orderByV($order = Criteria::ASC) Order by the v column
 *
 * @method TagsQuery groupById() Group by the id column
 * @method TagsQuery groupByType() Group by the type column
 * @method TagsQuery groupByK() Group by the k column
 * @method TagsQuery groupByV() Group by the v column
 *
 * @method TagsQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method TagsQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method TagsQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method Tags findOne(PropelPDO $con = null) Return the first Tags matching the query
 * @method Tags findOneOrCreate(PropelPDO $con = null) Return the first Tags matching the query, or a new Tags object populated from the query conditions when no match is found
 *
 * @method Tags findOneByType(string $type) Return the first Tags filtered by the type column
 * @method Tags findOneByK(string $k) Return the first Tags filtered by the k column
 * @method Tags findOneByV(string $v) Return the first Tags filtered by the v column
 *
 * @method array findById(string $id) Return Tags objects filtered by the id column
 * @method array findByType(string $type) Return Tags objects filtered by the type column
 * @method array findByK(string $k) Return Tags objects filtered by the k column
 * @method array findByV(string $v) Return Tags objects filtered by the v column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseTagsQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseTagsQuery object.
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
            $modelName = 'Tags';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new TagsQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   TagsQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return TagsQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof TagsQuery) {
            return $criteria;
        }
        $query = new TagsQuery(null, null, $modelAlias);

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
     * @return   Tags|Tags[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = TagsPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(TagsPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 Tags A model object, or null if the key is not found
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
     * @return                 Tags A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `type`, `k`, `v` FROM `tags` WHERE `id` = :p0';
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
            $obj = new Tags();
            $obj->hydrate($row);
            TagsPeer::addInstanceToPool($obj, (string) $key);
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
     * @return Tags|Tags[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|Tags[]|mixed the list of results, formatted by the current formatter
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
     * @return TagsQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(TagsPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return TagsQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(TagsPeer::ID, $keys, Criteria::IN);
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
     * @return TagsQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(TagsPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(TagsPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(TagsPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the type column
     *
     * Example usage:
     * <code>
     * $query->filterByType('fooValue');   // WHERE type = 'fooValue'
     * $query->filterByType('%fooValue%'); // WHERE type LIKE '%fooValue%'
     * </code>
     *
     * @param     string $type The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TagsQuery The current query, for fluid interface
     */
    public function filterByType($type = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($type)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $type)) {
                $type = str_replace('*', '%', $type);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TagsPeer::TYPE, $type, $comparison);
    }

    /**
     * Filter the query on the k column
     *
     * Example usage:
     * <code>
     * $query->filterByK('fooValue');   // WHERE k = 'fooValue'
     * $query->filterByK('%fooValue%'); // WHERE k LIKE '%fooValue%'
     * </code>
     *
     * @param     string $k The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TagsQuery The current query, for fluid interface
     */
    public function filterByK($k = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($k)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $k)) {
                $k = str_replace('*', '%', $k);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TagsPeer::K, $k, $comparison);
    }

    /**
     * Filter the query on the v column
     *
     * Example usage:
     * <code>
     * $query->filterByV('fooValue');   // WHERE v = 'fooValue'
     * $query->filterByV('%fooValue%'); // WHERE v LIKE '%fooValue%'
     * </code>
     *
     * @param     string $v The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return TagsQuery The current query, for fluid interface
     */
    public function filterByV($v = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($v)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $v)) {
                $v = str_replace('*', '%', $v);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(TagsPeer::V, $v, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   Tags $tags Object to remove from the list of results
     *
     * @return TagsQuery The current query, for fluid interface
     */
    public function prune($tags = null)
    {
        if ($tags) {
            $this->addUsingAlias(TagsPeer::ID, $tags->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
