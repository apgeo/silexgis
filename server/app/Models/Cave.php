<?php

namespace App\Models;

use Eloquent as Model;
use Illuminate\Database\Eloquent\SoftDeletes;
use Illuminate\Database\Eloquent\Factories\HasFactory;

/**
 * @OA\Schema(
 *      schema="Cave",
 *      required={"name", "cave_type_id", "user_id", "created_at", "updated_at"},
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
 *          property="cave_type_id",
 *          description="cave_type_id",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="identification_code",
 *          description="identification_code",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="description",
 *          description="description",
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
 *          property="other_toponyms",
 *          description="other_toponyms",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="rock_type_id",
 *          description="rock_type_id",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="rock_age",
 *          description="rock_age",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="hydrographic_basin",
 *          description="hydrographic_basin",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="valley",
 *          description="valley",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="tributary_river",
 *          description="tributary_river",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="closest_address",
 *          description="closest_address",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="is_show_cave",
 *          description="is_show_cave",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="show_cave_length",
 *          description="show_cave_length",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="website",
 *          description="website",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="land_registry_number",
 *          description="land_registry_number",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="region",
 *          description="region",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="depth",
 *          description="depth",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="surveyed_length",
 *          description="surveyed_length",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="discovery_date",
 *          description="discovery_date",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="discoverer",
 *          description="discoverer",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="volume",
 *          description="volume",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="area",
 *          description="area",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="positive_depth",
 *          description="positive_depth",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="negative_depth",
 *          description="negative_depth",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="ramification_index",
 *          description="ramification_index",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="real_extension",
 *          description="real_extension",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="cave_age",
 *          description="cave_age",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="projected_extension",
 *          description="projected_extension",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="exploration_status",
 *          description="exploration_status",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="protection_class",
 *          description="protection_class",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string"
 *      ),
 *      @OA\Property(
 *          property="potential_depth",
 *          description="potential_depth",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="estimated_length",
 *          description="estimated_length",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="altitude",
 *          description="altitude",
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
 *          property="delete_time",
 *          description="delete_time",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string",
 *          format="date-time"
 *      ),
 *      @OA\Property(
 *          property="is_not_saved",
 *          description="is_not_saved",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="integer",
 *          format="int32"
 *      ),
 *      @OA\Property(
 *          property="created_at",
 *          description="created_at",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string",
 *          format="date-time"
 *      ),
 *      @OA\Property(
 *          property="updated_at",
 *          description="updated_at",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string",
 *          format="date-time"
 *      ),
 *      @OA\Property(
 *          property="deleted_at",
 *          description="deleted_at",
 *          readOnly=$FIELD_READ_ONLY$,
 *          nullable=$FIELD_NULLABLE$,
 *          type="string",
 *          format="date-time"
 *      )
 * )
 */
class Cave extends Model
{
    use SoftDeletes;

    use HasFactory;

    public $table = 'caves';
    
    const CREATED_AT = 'created_at';
    const UPDATED_AT = 'updated_at';


protected $dates = ['deleted_at'/*, 'add_time', 'delete_time'*/];



    public $fillable = [
        'name',
        'cave_type_id',
        'identification_code',
        'description',
        'user_id',
        'other_toponyms',
        'rock_type_id',
        'rock_age',
        'hydrographic_basin',
        'valley',
        'tributary_river',
        'closest_address',
        'is_show_cave',
        'show_cave_length',
        'website',
        'land_registry_number',
        'region',
        'depth',
        'surveyed_length',
        'discovery_date',
        'discoverer',
        'volume',
        'area',
        'positive_depth',
        'negative_depth',
        'ramification_index',
        'real_extension',
        'cave_age',
        'projected_extension',
        'exploration_status',
        'protection_class',
        'potential_depth',
        'estimated_length',
        'altitude',
        'add_time',
        'delete_time',
        'is_not_saved'
    ];

    /**
     * The attributes that should be casted to native types.
     *
     * @var array
     */
    protected $casts = [
        'id' => 'integer',
        'name' => 'string',
        'cave_type_id' => 'integer',
        'identification_code' => 'string',
        'description' => 'string',
        'user_id' => 'integer',
        'other_toponyms' => 'string',
        'rock_type_id' => 'string',
        'rock_age' => 'string',
        'hydrographic_basin' => 'string',
        'valley' => 'string',
        'tributary_river' => 'string',
        'closest_address' => 'string',
        'is_show_cave' => 'integer',
        'show_cave_length' => 'integer',
        'website' => 'string',
        'land_registry_number' => 'string',
        'region' => 'string',
        'depth' => 'integer',
        'surveyed_length' => 'integer',
        'discovery_date' => 'string',
        'discoverer' => 'string',
        'volume' => 'integer',
        'area' => 'integer',
        'positive_depth' => 'integer',
        'negative_depth' => 'integer',
        'ramification_index' => 'integer',
        'real_extension' => 'integer',
        'cave_age' => 'integer',
        'projected_extension' => 'integer',
        'exploration_status' => 'string',
        'protection_class' => 'string',
        'potential_depth' => 'integer',
        'estimated_length' => 'integer',
        'altitude' => 'integer',
        'add_time' => 'datetime',
        'delete_time' => 'datetime',
        'is_not_saved' => 'integer'
    ];

    /**
     * Validation rules
     *
     * @var array
     */
    public static $rules = [
        'name' => 'required|string|max:255',
        'cave_type_id' => 'required',
        'identification_code' => 'nullable|string|max:50',
        'description' => 'nullable|string',
        'user_id' => 'required',
        'other_toponyms' => 'nullable|string|max:250',
        'rock_type_id' => 'nullable|string',
        'rock_age' => 'nullable|string|max:50',
        'hydrographic_basin' => 'nullable|string|max:100',
        'valley' => 'nullable|string|max:50',
        'tributary_river' => 'nullable|string|max:50',
        'closest_address' => 'nullable|string|max:200',
        'is_show_cave' => 'nullable',
        'show_cave_length' => 'nullable',
        'website' => 'nullable|string|max:255',
        'land_registry_number' => 'nullable|string|max:50',
        'region' => 'nullable|string|max:50',
        'depth' => 'nullable',
        'surveyed_length' => 'nullable|integer',
        'discovery_date' => 'nullable|string|max:50',
        'discoverer' => 'nullable|string|max:50',
        'volume' => 'nullable|integer',
        'area' => 'nullable|integer',
        'positive_depth' => 'nullable',
        'negative_depth' => 'nullable',
        'ramification_index' => 'nullable',
        'real_extension' => 'nullable|integer',
        'cave_age' => 'nullable|integer',
        'projected_extension' => 'nullable|integer',
        'exploration_status' => 'nullable|string',
        'protection_class' => 'nullable|string|max:20',
        'potential_depth' => 'nullable',
        'estimated_length' => 'nullable|integer',
        'altitude' => 'nullable',
        'add_time' => 'nullable',
        'delete_time' => 'nullable',
        'is_not_saved' => 'nullable|integer',

        // 'created_at' => 'required',
        // 'updated_at' => 'required',
        // 'deleted_at' => 'nullable'
    ];

    // public $timestamps = true;
}
