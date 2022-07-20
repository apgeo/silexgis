<?php

namespace App\Repositories;

use App\Models\TripLogs;
use App\Repositories\BaseRepository;

/**
 * Class TripLogsRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:40 pm UTC
*/

class TripLogsRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'add_time',
        'trip_start_time',
        'trip_end_time',
        'details',
        'target_zone',
        'type',
        'temporary',
        'summary',
        'title'
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
        return TripLogs::class;
    }
}
