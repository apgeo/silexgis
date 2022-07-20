<?php

namespace Database\Factories;

use App\Models\CaveType;
use Illuminate\Database\Eloquent\Factories\Factory;

class CaveTypeFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = CaveType::class;

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
