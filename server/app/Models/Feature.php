<?php

namespace App\Models;

use Eloquent as Model;
use Illuminate\Database\Eloquent\SoftDeletes;
use Illuminate\Database\Eloquent\Factories\HasFactory;

/**
 * @OA\Schema(
 *      schema="Feature",
 *      required={"point_geometry_id", "feature_type_id"},
 *      @OA\Property(
 *          property="id",
 *          description="id",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="name",
 *          description="name",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="point_geometry_id",
 *          description="point_geometry_id",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="description",
 *          description="description",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="feature_type_id",
 *          description="feature_type_id",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="properties",
 *          description="properties",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="user_id",
 *          description="user_id",
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
 *          property="tags",
 *          description="tags",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      )
 * )
 */
class Feature extends Model
{
    // use SoftDeletes;

    use HasFactory;

    public $table = 'features';
    
    const CREATED_AT = 'created_at';
    const UPDATED_AT = 'updated_at';
    // const DELETED_AT = 'deleted_at';
    

    protected $dates = ['deleted_at'];

    public $timestamps = false; //--added
    //const DELETED_AT = false;


    public $fillable = [
        'name',
        'point_geometry_id',
        'description',
        'feature_type_id',
        'properties',
        'user_id',
        'add_time',
        'tags'
    ];

    /**
     * The attributes that should be casted to native types.
     *
     * @var array
     */
    protected $casts = [
        'id' => 'integer',
        'name' => 'string',
        'point_geometry_id' => 'integer',
        'description' => 'string',
        'feature_type_id' => 'integer',
        'properties' => 'string',
        'user_id' => 'integer',
        'add_time' => 'datetime',
        'tags' => 'string'
    ];

    /**
     * Validation rules
     *
     * @var array
     */
    public static $rules = [
        'name' => 'nullable|string|max:100',
        // 'point_geometry_id' => 'required',
        'description' => 'nullable|string|max:1000',
        'feature_type_id' => 'required',
        'properties' => 'nullable|string',
        'user_id' => 'nullable',
        'add_time' => 'nullable',
        'tags' => 'nullable|string'
    ];

    
}
