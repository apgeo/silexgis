<?php

namespace App\Models;

use Eloquent as Model;
use Illuminate\Database\Eloquent\SoftDeletes;
use Illuminate\Database\Eloquent\Factories\HasFactory;

/**
 * @OA\Schema(
 *      schema="FeatureType",
 *      required={"name", "type", "group_title"},
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
 *          property="symbol_path",
 *          description="symbol_path",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="type",
 *          description="type",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="group_title",
 *          description="group_title",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="style_properties",
 *          description="style_properties",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="order_index",
 *          description="order_index",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="disabled",
 *          description="disabled",
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
 *          property="field_definitions",
 *          description="field_definitions",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="group_identifier",
 *          description="group_identifier",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      )
 * )
 */
class FeatureType extends Model
{
    // use SoftDeletes;

    use HasFactory;

    public $table = 'feature_types';
    
    const CREATED_AT = 'created_at';
    const UPDATED_AT = 'updated_at';

    public $timestamps = false; //--added
    protected $dates = ['deleted_at'];



    public $fillable = [
        'name',
        'symbol_path',
        'type',
        'group_title',
        'style_properties',
        'order_index',
        'disabled',
        'description',
        'field_definitions',
        'group_identifier'
    ];

    /**
     * The attributes that should be casted to native types.
     *
     * @var array
     */
    protected $casts = [
        'id' => 'integer',
        'name' => 'string',
        'symbol_path' => 'string',
        'type' => 'string',
        'group_title' => 'string',
        'style_properties' => 'string',
        'order_index' => 'integer',
        'disabled' => 'integer',
        'description' => 'string',
        'field_definitions' => 'string',
        'group_identifier' => 'string'
    ];

    /**
     * Validation rules
     *
     * @var array
     */
    public static $rules = [
        'name' => 'required|string|max:50',
        'symbol_path' => 'nullable|string|max:500',
        'type' => 'required|string',
        'group_title' => 'required|string|max:25',
        'style_properties' => 'nullable|string',
        'order_index' => 'nullable|integer',
        'disabled' => 'nullable|integer',
        'description' => 'nullable|string',
        'field_definitions' => 'nullable|string',
        'group_identifier' => 'nullable|string|max:20'
    ];

    /**
     * @return \Illuminate\Database\Eloquent\Relations\HasMany
     **/
    public function features()
    {
        return $this->hasMany(\App\Models\Feature::class, 'feature_type_id');
    }
}
