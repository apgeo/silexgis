<?php

namespace Database\Factories;

use App\Models\FeatureType;
use Illuminate\Database\Eloquent\Factories\Factory;

class FeatureTypeFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = FeatureType::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'name' => $this->faker->word,
        'symbol_path' => $this->faker->word,
        'type' => $this->faker->word,
        'group_title' => $this->faker->word,
        'style_properties' => $this->faker->text,
        'order_index' => $this->faker->randomDigitNotNull,
        'disabled' => $this->faker->randomDigitNotNull,
        'description' => $this->faker->text,
        'field_definitions' => $this->faker->word,
        'group_identifier' => $this->faker->word
        ];
    }
}
