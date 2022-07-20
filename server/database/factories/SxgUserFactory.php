<?php

namespace Database\Factories;

use App\Models\SxgUser;
use Illuminate\Database\Eloquent\Factories\Factory;

class SxgUserFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = SxgUser::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'username' => $this->faker->word,
        'password' => $this->faker->word,
        'email' => $this->faker->word,
        'admin_level' => $this->faker->randomDigitNotNull,
        'language' => $this->faker->word,
        'last_log_in_time' => $this->faker->date('Y-m-d H:i:s'),
        'add_time' => $this->faker->date('Y-m-d H:i:s'),
        'picture_storage_type' => $this->faker->word
        ];
    }
}
