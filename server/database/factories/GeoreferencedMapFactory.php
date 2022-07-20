<?php

namespace Database\Factories;

use App\Models\GeoreferencedMap;
use Illuminate\Database\Eloquent\Factories\Factory;

class GeoreferencedMapFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = GeoreferencedMap::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'description' => $this->faker->word,
        'boundary_north' => $this->faker->word,
        'boundary_east' => $this->faker->word,
        'boundary_south' => $this->faker->word,
        'boundary_west' => $this->faker->word,
        'image_id' => $this->faker->word,
        'enabled' => $this->faker->word,
        'title' => $this->faker->word
        ];
    }
}
