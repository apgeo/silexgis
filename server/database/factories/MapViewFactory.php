<?php

namespace Database\Factories;

use App\Models\MapView;
use Illuminate\Database\Eloquent\Factories\Factory;

class MapViewFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = MapView::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'name' => $this->faker->word,
        'properties' => $this->faker->text,
        'center_geometry' => $this->faker->word,
        'is_default' => $this->faker->word
        ];
    }
}
