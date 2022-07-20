<?php

namespace App\Repositories;

use App\Models\Feature;
use App\Repositories\BaseRepository;

/**
 * Class FeatureRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:36 pm UTC
*/

class FeatureRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'name',
        'point_geometry_id',
        'description',
        'feature_type_id',
        'properties',
        'user_id',
        'add_time',
        'tags'
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
        return Feature::class;
    }
}
