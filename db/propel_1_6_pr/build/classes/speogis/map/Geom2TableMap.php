<?php



/**
 * This class defines the structure of the 'geom2' table.
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
class Geom2TableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.Geom2TableMap';

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
        $this->setName('geom2');
        $this->setPhpName('Geom2');
        $this->setClassname('Geom2');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addColumn('g', 'G', 'VARCHAR', false, null, null);
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // Geom2TableMap
