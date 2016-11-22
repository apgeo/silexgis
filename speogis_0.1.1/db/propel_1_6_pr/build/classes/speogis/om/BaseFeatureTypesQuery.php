<?php


/**
 * Base class that represents a query for the 'feature_types' table.
 *
 *
 *
 * @method FeatureTypesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method FeatureTypesQuery orderByName($order = Criteria::ASC) Order by the name column
 * @method FeatureTypesQuery orderBySymbolPath($order = Criteria::ASC) Order by the symbol_path column
 * @method FeatureTypesQuery orderByType($order = Criteria::ASC) Order by the type column
 *
 * @method FeatureTypesQuery groupById() Group by the id column
 * @method FeatureTypesQuery groupByName() Group by the name column
 * @method FeatureTypesQuery groupBySymbolPath() Group by the symbol_path column
 * @method FeatureTypesQuery groupByType() Group by the type column
 *
 * @method FeatureTypesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method FeatureTypesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method FeatureTypesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method FeatureTypes findOne(PropelPDO $con = null) Return the first FeatureTypes matching the query
 * @method FeatureTypes findOneOrCreate(PropelPDO $con = null) Return the first FeatureTypes matching the query, or a new FeatureTypes object populated from the query conditions when no match is found
 *
 * @method FeatureTypes findOneByName(string $name) Return the first FeatureTypes filtered by the name column
 * @method FeatureTypes findOneBySymbolPath(string $symbol_path) Return the first FeatureTypes filtered by the symbol_path column
 * @method FeatureTypes findOneByType(string $type) Return the first FeatureTypes filtered by the type column
 *
 * @method array findById(string $id) Return FeatureTypes objects filtered by the id column
 * @method array findByName(string $name) Return FeatureTypes objects filtered by the name column
 * @method array findBySymbolPath(string $symbol_path) Return FeatureTypes objects filtered by the symbol_path column
 * @method array findByType(string $type) Return FeatureTypes objects filtered by the type column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseFeatureTypesQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseFeatureTypesQuery object.
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
            $modelName = 'FeatureTypes';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new FeatureTypesQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   FeatureTypesQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return FeatureTypesQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof FeatureTypesQuery) {
            return $criteria;
        }
        $query = new FeatureTypesQuery(null, null, $modelAlias);

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
     * @return   FeatureTypes|FeatureTypes[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = FeatureTypesPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(FeatureTypesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 FeatureTypes A model object, or null if the key is not found
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
     * @return                 FeatureTypes A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `name`, `symbol_path`, `type` FROM `feature_types` WHERE `id` = :p0';
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
            $obj = new FeatureTypes();
            $obj->hydrate($row);
            FeatureTypesPeer::addInstanceToPool($obj, (string) $key);
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
     * @return FeatureTypes|FeatureTypes[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|FeatureTypes[]|mixed the list of results, formatted by the current formatter
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
     * @return FeatureTypesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(FeatureTypesPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return FeatureTypesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(FeatureTypesPeer::ID, $keys, Criteria::IN);
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
     * @return FeatureTypesQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(FeatureTypesPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(FeatureTypesPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(FeatureTypesPeer::ID, $id, $comparison);
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
     * @return FeatureTypesQuery The current query, for fluid interface
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

        return $this->addUsingAlias(FeatureTypesPeer::NAME, $name, $comparison);
    }

    /**
     * Filter the query on the symbol_path column
     *
     * Example usage:
     * <code>
     * $query->filterBySymbolPath('fooValue');   // WHERE symbol_path = 'fooValue'
     * $query->filterBySymbolPath('%fooValue%'); // WHERE symbol_path LIKE '%fooValue%'
     * </code>
     *
     * @param     string $symbolPath The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return FeatureTypesQuery The current query, for fluid interface
     */
    public function filterBySymbolPath($symbolPath = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($symbolPath)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $symbolPath)) {
                $symbolPath = str_replace('*', '%', $symbolPath);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(FeatureTypesPeer::SYMBOL_PATH, $symbolPath, $comparison);
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
     * @return FeatureTypesQuery The current query, for fluid interface
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

        return $this->addUsingAlias(FeatureTypesPeer::TYPE, $type, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   FeatureTypes $featureTypes Object to remove from the list of results
     *
     * @return FeatureTypesQuery The current query, for fluid interface
     */
    public function prune($featureTypes = null)
    {
        if ($featureTypes) {
            $this->addUsingAlias(FeatureTypesPeer::ID, $featureTypes->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
