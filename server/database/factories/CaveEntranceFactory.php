<?php

namespace Database\Factories;

use App\Models\CaveEntrance;
use Illuminate\Database\Eloquent\Factories\Factory;

class CaveEntranceFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = CaveEntrance::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'name' => $this->faker->word,
        'entrance_type' => $this->faker->word,
        'description' => $this->faker->word,
        'is_main_entrance' => $this->faker->word,
        'hydrologic_type' => $this->faker->word,
        'cave_id' => $this->faker->word,
        'feature_id' => $this->faker->word
        ];
    }
}
