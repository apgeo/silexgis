<?php

namespace Database\Factories;

use App\Models\Image;
use Illuminate\Database\Eloquent\Factories\Factory;

class ImageFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = Image::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
        'file_path' => $this->faker->word,
        'user_id' => $this->faker->word,
        'add_time' => $this->faker->date('Y-m-d H:i:s'),
        'point_geometry_id' => $this->faker->word,
        'description' => $this->faker->word,
        'thumb_file_path' => $this->faker->word,
        'picture_storage_type' => $this->faker->word
        ];
    }
}
