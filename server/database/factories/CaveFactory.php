<?php

namespace Database\Factories;

use App\Models\Cave;
use Illuminate\Database\Eloquent\Factories\Factory;

class CaveFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = Cave::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'name' => $this->faker->word,
        'cave_type_id' => $this->faker->word,
        'identification_code' => $this->faker->word,
        'description' => $this->faker->text,
        'user_id' => $this->faker->word,
        'other_toponyms' => $this->faker->word,
        'rock_type_id' => $this->faker->word,
        'rock_age' => $this->faker->word,
        'hydrographic_basin' => $this->faker->word,
        'valley' => $this->faker->word,
        'tributary_river' => $this->faker->word,
        'closest_address' => $this->faker->word,
        'is_show_cave' => $this->faker->word,
        'show_cave_length' => $this->faker->word,
        'website' => $this->faker->word,
        'land_registry_number' => $this->faker->word,
        'region' => $this->faker->word,
        'depth' => $this->faker->word,
        'surveyed_length' => $this->faker->randomDigitNotNull,
        'discovery_date' => $this->faker->word,
        'discoverer' => $this->faker->word,
        'volume' => $this->faker->randomDigitNotNull,
        'area' => $this->faker->randomDigitNotNull,
        'positive_depth' => $this->faker->word,
        'negative_depth' => $this->faker->word,
        'ramification_index' => $this->faker->word,
        'real_extension' => $this->faker->randomDigitNotNull,
        'cave_age' => $this->faker->randomDigitNotNull,
        'projected_extension' => $this->faker->randomDigitNotNull,
        'exploration_status' => $this->faker->word,
        'protection_class' => $this->faker->word,
        'potential_depth' => $this->faker->word,
        'estimated_length' => $this->faker->randomDigitNotNull,
        'altitude' => $this->faker->word,
        'add_time' => $this->faker->date('Y-m-d H:i:s'),
        'delete_time' => $this->faker->date('Y-m-d H:i:s'),
        'is_not_saved' => $this->faker->randomDigitNotNull,
        'created_at' => $this->faker->date('Y-m-d H:i:s'),
        'updated_at' => $this->faker->date('Y-m-d H:i:s'),
        'deleted_at' => $this->faker->date('Y-m-d H:i:s')
        ];
    }
}
