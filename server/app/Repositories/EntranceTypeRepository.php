<?php

namespace App\Repositories;

use App\Models\EntranceType;
use App\Repositories\BaseRepository;

/**
 * Class EntranceTypeRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:38 pm UTC
*/

class EntranceTypeRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'name'
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
        return EntranceType::class;
    }
}
