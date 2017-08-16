<?php



/**
 * This class defines the structure of the 'images' table.
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
class ImagesTableMap extends TableMap
{

    /**
     * The (dot-path) name of this class
     */
    const CLASS_NAME = 'speogis.map.ImagesTableMap';

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
        $this->setName('images');
        $this->setPhpName('Images');
        $this->setClassname('Images');
        $this->setPackage('speogis');
        $this->setUseIdGenerator(true);
        // columns
        $this->addPrimaryKey('id', 'Id', 'BIGINT', true, null, null);
        $this->addColumn('file_path', 'FilePath', 'VARCHAR', true, 2000, null);
        $this->addColumn('user_id', 'UserId', 'BIGINT', false, null, null);
        $this->addColumn('add_time', 'AddTime', 'TIMESTAMP', false, null, null);
        $this->addColumn('point_id', 'PointId', 'BIGINT', false, null, null);
        $this->addColumn('description', 'Description', 'VARCHAR', false, 500, null);
        $this->addColumn('thumb_file_path', 'ThumbFilePath', 'VARCHAR', true, 2000, null);
        // validators
    } // initialize()

    /**
     * Build the RelationMap objects for this table relationships
     */
    public function buildRelations()
    {
    } // buildRelations()

} // ImagesTableMap
