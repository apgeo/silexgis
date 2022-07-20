<?php

namespace App\Models;

use Eloquent as Model;
use Illuminate\Database\Eloquent\SoftDeletes;
use Illuminate\Database\Eloquent\Factories\HasFactory;

/**
 * @OA\Schema(
 *      schema="Point",
 *      required={"added_by_user_id", "add_time"},
 *      @OA\Property(
 *          property="id",
 *          description="id",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="lat_long",
 *          description="lat_long",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="elevation",
 *          description="elevation",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="gpx_name",
 *          description="gpx_name",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="gpx_sym",
 *          description="gpx_sym",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="gpx_type",
 *          description="gpx_type",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="gpx_cmt",
 *          description="gpx_cmt",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="gpx_sat",
 *          description="gpx_sat",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="gpx_fix",
 *          description="gpx_fix",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="gpx_time",
 *          description="gpx_time",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string",
 *          format="date-time"
 *      ),
 *      @OA\Property(
 *          property="_type",
 *          description="_type",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="_details",
 *          description="_details",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="added_by_user_id",
 *          description="added_by_user_id",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="add_time",
 *          description="add_time",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string",
 *          format="date-time"
 *      ),
 *      @OA\Property(
 *          property="_id_point_type",
 *          description="_id_point_type",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="spatial_geometry",
 *          description="spatial_geometry",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="update_time",
 *          description="update_time",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string",
 *          format="date-time"
 *      )
 * )
 */
class Point extends Model
{
    use SoftDeletes;

    use HasFactory;

    public $table = 'points';
    
    const CREATED_AT = 'created_at';
    const UPDATED_AT = 'updated_at';


    protected $dates = ['deleted_at'];



    public $fillable = [
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
     * The attributes that should be casted to native types.
     *
     * @var array
     */
    protected $casts = [
        'id' => 'integer',
        'lat_long' => 'string',
        'elevation' => 'integer',
        'gpx_name' => 'string',
        'gpx_sym' => 'string',
        'gpx_type' => 'string',
        'gpx_cmt' => 'string',
        'gpx_sat' => 'integer',
        'gpx_fix' => 'string',
        'gpx_time' => 'datetime',
        '_type' => 'integer',
        '_details' => 'string',
        'added_by_user_id' => 'integer',
        'add_time' => 'datetime',
        '_id_point_type' => 'integer',
        'spatial_geometry' => 'string',
        'update_time' => 'datetime'
    ];

    /**
     * Validation rules
     *
     * @var array
     */
    public static $rules = [
        'lat_long' => 'nullable|string',
        'elevation' => 'nullable|integer',
        'gpx_name' => 'nullable|string|max:500',
        'gpx_sym' => 'nullable|string|max:50',
        'gpx_type' => 'nullable|string|max:50',
        'gpx_cmt' => 'nullable|string|max:500',
        'gpx_sat' => 'nullable|integer',
        'gpx_fix' => 'nullable|string|max:8',
        'gpx_time' => 'nullable',
        '_type' => 'nullable|integer',
        '_details' => 'nullable|string|max:5000',
        'added_by_user_id' => 'required',
        'add_time' => 'required',
        '_id_point_type' => 'nullable',
        'spatial_geometry' => 'nullable|string',
        'update_time' => 'nullable'
    ];

    
}
