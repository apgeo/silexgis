<?php

namespace Database\Factories;

use App\Models\Feature;
use Illuminate\Database\Eloquent\Factories\Factory;

class FeatureFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = Feature::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
        'name' => $this->faker->word,
        'point_geometry_id' => $this->faker->word,
        'description' => $this->faker->word,
        'feature_type_id' => $this->faker->word,
        'properties' => $this->faker->text,
        'user_id' => $this->faker->word,
        'add_time' => $this->faker->date('Y-m-d H:i:s'),
        'tags' => $this->faker->text
        ];
    }
}
