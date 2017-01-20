<?php



/**
 * This class defines the structure of the 'feature_types' table.
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
class FeatureTypesTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.FeatureTypesTableMap';

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
        $this->setName('feature_types');
        $this->setPhpName('FeatureTypes');
        $this->setClassname('FeatureTypes');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('name', 'Name', 'VARCHAR', true, 50, null);
        $this->addColumn('symbol_path', 'SymbolPath', 'VARCHAR', false, 500, null);
        $this->addColumn('type', 'Type', 'CHAR', true, null, null);
        $this->getColumn('type', false)->setValueSet(array (
  0 => 'point',
  1 => 'linestring',
  2 => 'polygon',
));
        $this->addColumn('group_type', 'GroupType', 'VARCHAR', false, 20, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // FeatureTypesTableMap
