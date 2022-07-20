<?php

namespace App\Repositories;

use App\Models\Point;
use App\Repositories\BaseRepository;

/**
 * Class PointRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:39 pm UTC
*/

class PointRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'lat_long',
        'elevation',
        'gpx_name',
        'gpx_sym',
        'gpx_type',
        'gpx_cmt',
        'gpx_sat',
        'gpx_fix',
        'gpx_time',
        '_type',
        '_details',
        'added_by_user_id',
        'add_time',
        '_id_point_type',
        'spatial_geometry',
        'update_time'
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
        return Point::class;
    }
}
