<?php

namespace App\Repositories;

use App\Models\CaveEntrance;
use App\Repositories\BaseRepository;

/**
 * Class CaveEntranceRepository
 * @package App\Repositories
 * @version July 19, 2022, 7:35 pm UTC
*/

class CaveEntranceRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'name',
        'entrance_type',
        'description',
        'is_main_entrance',
        'hydrologic_type',
        'cave_id',
        'feature_id'
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
        return CaveEntrance::class;
    }
}
