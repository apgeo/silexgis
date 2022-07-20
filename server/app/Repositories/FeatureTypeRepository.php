<?php

namespace App\Repositories;

use App\Models\FeatureType;
use App\Repositories\BaseRepository;

/**
 * Class FeatureTypeRepository
 * @package App\Repositories
 * @version July 20, 2022, 5:22 am UTC
*/

class FeatureTypeRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'name',
        'symbol_path',
        'type',
        'group_title',
        'style_properties',
        'order_index',
        'disabled',
        'description',
        'field_definitions',
        'group_identifier'
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
        return FeatureType::class;
    }
}
