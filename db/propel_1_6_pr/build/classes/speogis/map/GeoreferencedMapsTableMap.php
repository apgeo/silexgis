<?php



/**
 * This class defines the structure of the 'georeferenced_maps' table.
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
class GeoreferencedMapsTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.GeoreferencedMapsTableMap';

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
        $this->setName('georeferenced_maps');
        $this->setPhpName('GeoreferencedMaps');
        $this->setClassname('GeoreferencedMaps');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('description', 'Description', 'VARCHAR', false, 500, null);
        $this->addColumn('boundary_north', 'BoundaryNorth', 'DECIMAL', true, 9, null);
        $this->addColumn('boundary_east', 'BoundaryEast', 'DECIMAL', true, 9, null);
        $this->addColumn('boundary_south', 'BoundarySouth', 'DECIMAL', true, 9, null);
        $this->addColumn('boundary_west', 'BoundaryWest', 'DECIMAL', true, 9, null);
        $this->addColumn('image_id', 'ImageId', 'BIGINT', true, null, null);
        $this->addColumn('enabled', 'Enabled', 'VARCHAR', true, 1, 'b\'1\'');
        $this->addColumn('title', 'Title', 'VARCHAR', false, 90, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // GeoreferencedMapsTableMap
