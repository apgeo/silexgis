<?php

namespace Database\Factories;

use App\Models\Geofile;
use Illuminate\Database\Eloquent\Factories\Factory;

class GeofileFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = Geofile::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'file_name' => $this->faker->word,
        'id_user' => $this->faker->word,
        'add_time' => $this->faker->date('Y-m-d H:i:s'),
        'type' => $this->faker->word,
        'size' => $this->faker->randomDigitNotNull,
        'md5_hash' => $this->faker->word,
        'enabled' => $this->faker->word,
        'extract_style' => $this->faker->word
        ];
    }
}
