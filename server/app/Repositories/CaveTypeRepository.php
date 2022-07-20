<?php

namespace App\Repositories;

use App\Models\CaveType;
use App\Repositories\BaseRepository;

/**
 * Class CaveTypeRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:38 pm UTC
*/

class CaveTypeRepository extends BaseRepository
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
        return CaveType::class;
    }
}
