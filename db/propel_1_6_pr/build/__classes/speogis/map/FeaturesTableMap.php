<?php



/**
 * This class defines the structure of the 'features' table.
 *
 *
 *
 * This map class is used by Propel to do runtime db structure discovery.
 * For example, the createSelectSql() method checks the type of a given column used in an
 * ORDER BY clause to know whether it needs to apply SQL to make the ORDER BY case-insensitive
 * (i.e. if it's a text column type).
 *
 * @package    propel.generator.speogis.map
 */
class FeaturesTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.FeaturesTableMap';

    /**
     * Initialize the table attributes, columns and validators
     * Relations are not initialized by this method since they are lazy loaded
     *
     * @return void
     * @throws PropelException
     */
    public function initialize()
    {
        // attributes
        $this->setName('features');
        $this->setPhpName('Features');
        $this->setClassname('Features');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('name', 'Name', 'VARCHAR', false, 100, null);
        $this->addColumn('point_id', 'PointId', 'BIGINT', true, null, null);
        $this->addColumn('description', 'Description', 'VARCHAR', false, 1000, null);
        $this->addColumn('feature_type_id', 'FeatureTypeId', 'BIGINT', true, null, null);
        $this->addColumn('properties', 'Properties', 'LONGVARCHAR', false, null, null);
        $this->addColumn('user_id', 'UserId', 'BIGINT', false, null, null);
        $this->addColumn('add_time', 'AddTime', 'TIMESTAMP', false, null, null);
        $this->addColumn('tags', 'Tags', 'LONGVARCHAR', false, null, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // FeaturesTableMap
