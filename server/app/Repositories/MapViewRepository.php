<?php

namespace App\Repositories;

use App\Models\MapView;
use App\Repositories\BaseRepository;

/**
 * Class MapViewRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:38 pm UTC
*/

class MapViewRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'name',
        'properties',
        'center_geometry',
        'is_default'
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
        return MapView::class;
    }
}
