<?php

namespace App\Repositories;

use App\Models\SxgUser;
use App\Repositories\BaseRepository;

/**
 * Class SxgUserRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:38 pm UTC
*/

class SxgUserRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'username',
        'password',
        'email',
        'admin_level',
        'language',
        'last_log_in_time',
        'add_time',
        'picture_storage_type'
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
        return SxgUser::class;
    }
}
