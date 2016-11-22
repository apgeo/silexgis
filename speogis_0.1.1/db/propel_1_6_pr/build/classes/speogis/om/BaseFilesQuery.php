<?php


/**
 * Base class that represents a query for the 'files' table.
 *
 *
 *
 * @method FilesQuery orderById($order = Criteria::ASC) Order by the id column
 * @method FilesQuery orderByFileName($order = Criteria::ASC) Order by the file_name column
 * @method FilesQuery orderByIdUser($order = Criteria::ASC) Order by the id_user column
 * @method FilesQuery orderByAddTime($order = Criteria::ASC) Order by the add_time column
 * @method FilesQuery orderByType($order = Criteria::ASC) Order by the type column
 * @method FilesQuery orderBySize($order = Criteria::ASC) Order by the size column
 * @method FilesQuery orderByMd5Hash($order = Criteria::ASC) Order by the md5_hash column
 *
 * @method FilesQuery groupById() Group by the id column
 * @method FilesQuery groupByFileName() Group by the file_name column
 * @method FilesQuery groupByIdUser() Group by the id_user column
 * @method FilesQuery groupByAddTime() Group by the add_time column
 * @method FilesQuery groupByType() Group by the type column
 * @method FilesQuery groupBySize() Group by the size column
 * @method FilesQuery groupByMd5Hash() Group by the md5_hash column
 *
 * @method FilesQuery leftJoin($relation) Adds a LEFT JOIN clause to the query
 * @method FilesQuery rightJoin($relation) Adds a RIGHT JOIN clause to the query
 * @method FilesQuery innerJoin($relation) Adds a INNER JOIN clause to the query
 *
 * @method Files findOne(PropelPDO $con = null) Return the first Files matching the query
 * @method Files findOneOrCreate(PropelPDO $con = null) Return the first Files matching the query, or a new Files object populated from the query conditions when no match is found
 *
 * @method Files findOneByFileName(string $file_name) Return the first Files filtered by the file_name column
 * @method Files findOneByIdUser(string $id_user) Return the first Files filtered by the id_user column
 * @method Files findOneByAddTime(string $add_time) Return the first Files filtered by the add_time column
 * @method Files findOneByType(string $type) Return the first Files filtered by the type column
 * @method Files findOneBySize(int $size) Return the first Files filtered by the size column
 * @method Files findOneByMd5Hash(string $md5_hash) Return the first Files filtered by the md5_hash column
 *
 * @method array findById(string $id) Return Files objects filtered by the id column
 * @method array findByFileName(string $file_name) Return Files objects filtered by the file_name column
 * @method array findByIdUser(string $id_user) Return Files objects filtered by the id_user column
 * @method array findByAddTime(string $add_time) Return Files objects filtered by the add_time column
 * @method array findByType(string $type) Return Files objects filtered by the type column
 * @method array findBySize(int $size) Return Files objects filtered by the size column
 * @method array findByMd5Hash(string $md5_hash) Return Files objects filtered by the md5_hash column
 *
 * @package    propel.generator.speogis.om
 */
abstract class BaseFilesQuery extends ModelCriteria
{
    /**
     * Initializes internal state of BaseFilesQuery object.
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
            $modelName = 'Files';
        }
        parent::__construct($dbName, $modelName, $modelAlias);
    }

    /**
     * Returns a new FilesQuery object.
     *
     * @param     string $modelAlias The alias of a model in the query
     * @param   FilesQuery|Criteria $criteria Optional Criteria to build the query from
     *
     * @return FilesQuery
     */
    public static function create($modelAlias = null, $criteria = null)
    {
        if ($criteria instanceof FilesQuery) {
            return $criteria;
        }
        $query = new FilesQuery(null, null, $modelAlias);

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
     * @return   Files|Files[]|mixed the result, formatted by the current formatter
     */
    public function findPk($key, $con = null)
    {
        if ($key === null) {
            return null;
        }
        if ((null !== ($obj = FilesPeer::getInstanceFromPool((string) $key))) && !$this->formatter) {
            // the object is already in the instance pool
            return $obj;
        }
        if ($con === null) {
            $con = Propel::getConnection(FilesPeer::DATABASE_NAME, Propel::CONNECTION_READ);
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
     * @return                 Files A model object, or null if the key is not found
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
     * @return                 Files A model object, or null if the key is not found
     * @throws PropelException
     */
    protected function findPkSimple($key, $con)
    {
        $sql = 'SELECT `id`, `file_name`, `id_user`, `add_time`, `type`, `size`, `md5_hash` FROM `files` WHERE `id` = :p0';
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
            $obj = new Files();
            $obj->hydrate($row);
            FilesPeer::addInstanceToPool($obj, (string) $key);
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
     * @return Files|Files[]|mixed the result, formatted by the current formatter
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
     * @return PropelObjectCollection|Files[]|mixed the list of results, formatted by the current formatter
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
     * @return FilesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKey($key)
    {

        return $this->addUsingAlias(FilesPeer::ID, $key, Criteria::EQUAL);
    }

    /**
     * Filter the query by a list of primary keys
     *
     * @param     array $keys The list of primary key to use for the query
     *
     * @return FilesQuery The current query, for fluid interface
     */
    public function filterByPrimaryKeys($keys)
    {

        return $this->addUsingAlias(FilesPeer::ID, $keys, Criteria::IN);
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
     * @return FilesQuery The current query, for fluid interface
     */
    public function filterById($id = null, $comparison = null)
    {
        if (is_array($id)) {
            $useMinMax = false;
            if (isset($id['min'])) {
                $this->addUsingAlias(FilesPeer::ID, $id['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($id['max'])) {
                $this->addUsingAlias(FilesPeer::ID, $id['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(FilesPeer::ID, $id, $comparison);
    }

    /**
     * Filter the query on the file_name column
     *
     * Example usage:
     * <code>
     * $query->filterByFileName('fooValue');   // WHERE file_name = 'fooValue'
     * $query->filterByFileName('%fooValue%'); // WHERE file_name LIKE '%fooValue%'
     * </code>
     *
     * @param     string $fileName The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return FilesQuery The current query, for fluid interface
     */
    public function filterByFileName($fileName = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($fileName)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $fileName)) {
                $fileName = str_replace('*', '%', $fileName);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(FilesPeer::FILE_NAME, $fileName, $comparison);
    }

    /**
     * Filter the query on the id_user column
     *
     * Example usage:
     * <code>
     * $query->filterByIdUser(1234); // WHERE id_user = 1234
     * $query->filterByIdUser(array(12, 34)); // WHERE id_user IN (12, 34)
     * $query->filterByIdUser(array('min' => 12)); // WHERE id_user >= 12
     * $query->filterByIdUser(array('max' => 12)); // WHERE id_user <= 12
     * </code>
     *
     * @param     mixed $idUser The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return FilesQuery The current query, for fluid interface
     */
    public function filterByIdUser($idUser = null, $comparison = null)
    {
        if (is_array($idUser)) {
            $useMinMax = false;
            if (isset($idUser['min'])) {
                $this->addUsingAlias(FilesPeer::ID_USER, $idUser['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($idUser['max'])) {
                $this->addUsingAlias(FilesPeer::ID_USER, $idUser['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(FilesPeer::ID_USER, $idUser, $comparison);
    }

    /**
     * Filter the query on the add_time column
     *
     * Example usage:
     * <code>
     * $query->filterByAddTime('2011-03-14'); // WHERE add_time = '2011-03-14'
     * $query->filterByAddTime('now'); // WHERE add_time = '2011-03-14'
     * $query->filterByAddTime(array('max' => 'yesterday')); // WHERE add_time < '2011-03-13'
     * </code>
     *
     * @param     mixed $addTime The value to use as filter.
     *              Values can be integers (unix timestamps), DateTime objects, or strings.
     *              Empty strings are treated as NULL.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return FilesQuery The current query, for fluid interface
     */
    public function filterByAddTime($addTime = null, $comparison = null)
    {
        if (is_array($addTime)) {
            $useMinMax = false;
            if (isset($addTime['min'])) {
                $this->addUsingAlias(FilesPeer::ADD_TIME, $addTime['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($addTime['max'])) {
                $this->addUsingAlias(FilesPeer::ADD_TIME, $addTime['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(FilesPeer::ADD_TIME, $addTime, $comparison);
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
     * @return FilesQuery The current query, for fluid interface
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

        return $this->addUsingAlias(FilesPeer::TYPE, $type, $comparison);
    }

    /**
     * Filter the query on the size column
     *
     * Example usage:
     * <code>
     * $query->filterBySize(1234); // WHERE size = 1234
     * $query->filterBySize(array(12, 34)); // WHERE size IN (12, 34)
     * $query->filterBySize(array('min' => 12)); // WHERE size >= 12
     * $query->filterBySize(array('max' => 12)); // WHERE size <= 12
     * </code>
     *
     * @param     mixed $size The value to use as filter.
     *              Use scalar values for equality.
     *              Use array values for in_array() equivalent.
     *              Use associative array('min' => $minValue, 'max' => $maxValue) for intervals.
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return FilesQuery The current query, for fluid interface
     */
    public function filterBySize($size = null, $comparison = null)
    {
        if (is_array($size)) {
            $useMinMax = false;
            if (isset($size['min'])) {
                $this->addUsingAlias(FilesPeer::SIZE, $size['min'], Criteria::GREATER_EQUAL);
                $useMinMax = true;
            }
            if (isset($size['max'])) {
                $this->addUsingAlias(FilesPeer::SIZE, $size['max'], Criteria::LESS_EQUAL);
                $useMinMax = true;
            }
            if ($useMinMax) {
                return $this;
            }
            if (null === $comparison) {
                $comparison = Criteria::IN;
            }
        }

        return $this->addUsingAlias(FilesPeer::SIZE, $size, $comparison);
    }

    /**
     * Filter the query on the md5_hash column
     *
     * Example usage:
     * <code>
     * $query->filterByMd5Hash('fooValue');   // WHERE md5_hash = 'fooValue'
     * $query->filterByMd5Hash('%fooValue%'); // WHERE md5_hash LIKE '%fooValue%'
     * </code>
     *
     * @param     string $md5Hash The value to use as filter.
     *              Accepts wildcards (* and % trigger a LIKE)
     * @param     string $comparison Operator to use for the column comparison, defaults to Criteria::EQUAL
     *
     * @return FilesQuery The current query, for fluid interface
     */
    public function filterByMd5Hash($md5Hash = null, $comparison = null)
    {
        if (null === $comparison) {
            if (is_array($md5Hash)) {
                $comparison = Criteria::IN;
            } elseif (preg_match('/[\%\*]/', $md5Hash)) {
                $md5Hash = str_replace('*', '%', $md5Hash);
                $comparison = Criteria::LIKE;
            }
        }

        return $this->addUsingAlias(FilesPeer::MD5_HASH, $md5Hash, $comparison);
    }

    /**
     * Exclude object from result
     *
     * @param   Files $files Object to remove from the list of results
     *
     * @return FilesQuery The current query, for fluid interface
     */
    public function prune($files = null)
    {
        if ($files) {
            $this->addUsingAlias(FilesPeer::ID, $files->getId(), Criteria::NOT_EQUAL);
        }

        return $this;
    }

}
