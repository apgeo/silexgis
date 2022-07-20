<?php

namespace App\Repositories;

use App\Models\Asset;
use App\Repositories\BaseRepository;

/**
 * Class AssetRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:37 pm UTC
*/

class AssetRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'user_id',
        'uuid',
        'type',
        'path',
        'mime'
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
        return Asset::class;
    }
}
