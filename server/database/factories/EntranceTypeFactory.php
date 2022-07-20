<?php

namespace Database\Factories;

use App\Models\EntranceType;
use Illuminate\Database\Eloquent\Factories\Factory;

class EntranceTypeFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = EntranceType::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'name' => $this->faker->word
        ];
    }
}
