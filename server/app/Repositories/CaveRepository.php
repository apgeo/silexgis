<?php

namespace App\Repositories;

use App\Models\Cave;
use App\Repositories\BaseRepository;

/**
 * Class CaveRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:38 pm UTC
*/

class CaveRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'name',
        'cave_type_id',
        'identification_code',
        'description',
        'user_id',
        'other_toponyms',
        'rock_type_id',
        'rock_age',
        'hydrographic_basin',
        'valley',
        'tributary_river',
        'closest_address',
        'is_show_cave',
        'show_cave_length',
        'website',
        'land_registry_number',
        'region',
        'depth',
        'surveyed_length',
        'discovery_date',
        'discoverer',
        'volume',
        'area',
        'positive_depth',
        'negative_depth',
        'ramification_index',
        'real_extension',
        'cave_age',
        'projected_extension',
        'exploration_status',
        'protection_class',
        'potential_depth',
        'estimated_length',
        'altitude',
        'add_time',
        'delete_time',
        'is_not_saved'
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
        return Cave::class;
    }
}
