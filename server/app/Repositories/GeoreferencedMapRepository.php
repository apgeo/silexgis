<?php

namespace App\Repositories;

use App\Models\GeoreferencedMap;
use App\Repositories\BaseRepository;

/**
 * Class GeoreferencedMapRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:37 pm UTC
*/

class GeoreferencedMapRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'description',
        'boundary_north',
        'boundary_east',
        'boundary_south',
        'boundary_west',
        'image_id',
        'enabled',
        'title'
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
        return GeoreferencedMap::class;
    }
}
