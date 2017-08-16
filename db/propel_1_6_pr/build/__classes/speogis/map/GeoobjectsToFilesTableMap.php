<?php



/**
 * This class defines the structure of the 'geoobjects_to_files' table.
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
class GeoobjectsToFilesTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.GeoobjectsToFilesTableMap';

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
        $this->setName('geoobjects_to_files');
        $this->setPhpName('GeoobjectsToFiles');
        $this->setClassname('GeoobjectsToFiles');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('file_id', 'FileId', 'BIGINT', true, null, null);
        $this->addColumn('geoobject_id', 'GeoobjectId', 'BIGINT', true, null, null);
        $this->addColumn('geoobject_type', 'GeoobjectType', 'CHAR', true, null, null);
        $this->getColumn('geoobject_type', false)->setValueSet(array (
  0 => 'cave',
  1 => 'feature',
  2 => 'cave_entry',
));
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // GeoobjectsToFilesTableMap
