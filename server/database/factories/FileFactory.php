<?php

namespace Database\Factories;

use App\Models\File;
use Illuminate\Database\Eloquent\Factories\Factory;

class FileFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = File::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'file_name' => $this->faker->word,
        'user_id' => $this->faker->word,
        'add_time' => $this->faker->date('Y-m-d H:i:s'),
        'file_type' => $this->faker->word,
        'size' => $this->faker->randomDigitNotNull,
        'md5_hash' => $this->faker->word,
        'mime_type' => $this->faker->word
        ];
    }
}
