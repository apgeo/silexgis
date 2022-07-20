<?php

namespace App\Repositories;

use App\Models\TeamMember;
use App\Repositories\BaseRepository;

/**
 * Class TeamMemberRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:39 pm UTC
*/

class TeamMemberRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'first_name',
        'last_name',
        'nickname',
        'group_id',
        'picture_file_name',
        'add_time',
        'description',
        'email',
        'phone_number',
        'notes',
        'connected_user_id'
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
        return TeamMember::class;
    }
}
