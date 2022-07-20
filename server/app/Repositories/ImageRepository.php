<?php

namespace App\Repositories;

use App\Models\Image;
use App\Repositories\BaseRepository;

/**
 * Class ImageRepository
 * @package App\Repositories
 * @version June 28, 2022, 4:37 pm UTC
*/

class ImageRepository extends BaseRepository
{
    /**
     * @var array
     */
    protected $fieldSearchable = [
        'file_path',
        'user_id',
        'add_time',
        'point_geometry_id',
        'description',
        'thumb_file_path',
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
        return Image::class;
    }
}
