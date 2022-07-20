<?php

namespace App\Repositories;

use App\Models\Geofile;
use App\Repositories\BaseRepository;

/**
 * Class GeofileRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:37 pm UTC
*/

class GeofileRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'file_name',
        'id_user',
        'add_time',
        'type',
        'size',
        'md5_hash',
        'enabled',
        'extract_style'
    ];

    /**
     * Return searchable fields
     *
     * @return array
     */
    public function getFieldsSearchable()
    {
        return $this->fieldSearchable;
    }

    /**
     * Configure the Model
     **/
    public function model()
    {
        return Geofile::class;
    }
}
