<?php

namespace App\Models;

use Eloquent as Model;
use Illuminate\Database\Eloquent\SoftDeletes;
use Illuminate\Database\Eloquent\Factories\HasFactory;

/**
 * @OA\Schema(
 *      schema="GeoreferencedMap",
 *      required={"boundary_north", "boundary_east", "boundary_south", "boundary_west", "image_id", "enabled"},
 *      @OA\Property(
 *          property="id",
 *          description="id",
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
 *          property="boundary_north",
 *          description="boundary_north",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="number",
 *          format="number"
 *      ),
 *      @OA\Property(
 *          property="boundary_east",
 *          description="boundary_east",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="number",
 *          format="number"
 *      ),
 *      @OA\Property(
 *          property="boundary_south",
 *          description="boundary_south",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="number",
 *          format="number"
 *      ),
 *      @OA\Property(
 *          property="boundary_west",
 *          description="boundary_west",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="number",
 *          format="number"
 *      ),
 *      @OA\Property(
 *          property="image_id",
 *          description="image_id",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="enabled",
 *          description="enabled",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="boolean"
 *      ),
 *      @OA\Property(
 *          property="title",
 *          description="title",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      )
 * )
 */
class GeoreferencedMap extends Model
{
    use SoftDeletes;

    use HasFactory;

    public $table = 'georeferenced_maps';
    
    const CREATED_AT = 'created_at';
    const UPDATED_AT = 'updated_at';


    protected $dates = ['deleted_at'];



    public $fillable = [
        'description',
        'boundary_north',
        'boundary_east',
        'boundary_south',
        'boundary_west',
        'image_id',
        'enabled',
        'title'
    ];

    /**
     * The attributes that should be casted to native types.
     *
     * @var array
     */
    protected $casts = [
        'id' => 'integer',
        'description' => 'string',
        'boundary_north' => 'decimal:6',
        'boundary_east' => 'decimal:6',
        'boundary_south' => 'decimal:6',
        'boundary_west' => 'decimal:6',
        'image_id' => 'integer',
        'enabled' => 'boolean',
        'title' => 'string'
    ];

    /**
     * Validation rules
     *
     * @var array
     */
    public static $rules = [
        'description' => 'nullable|string|max:500',
        'boundary_north' => 'required|numeric',
        'boundary_east' => 'required|numeric',
        'boundary_south' => 'required|numeric',
        'boundary_west' => 'required|numeric',
        'image_id' => 'required',
        'enabled' => 'required|boolean',
        'title' => 'nullable|string|max:90'
    ];

    
}
