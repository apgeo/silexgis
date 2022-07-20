<?php

namespace Database\Factories;

use App\Models\Point;
use Illuminate\Database\Eloquent\Factories\Factory;

class PointFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = Point::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'lat_long' => $this->faker->word,
        'elevation' => $this->faker->randomDigitNotNull,
        'gpx_name' => $this->faker->word,
        'gpx_sym' => $this->faker->word,
        'gpx_type' => $this->faker->word,
        'gpx_cmt' => $this->faker->word,
        'gpx_sat' => $this->faker->randomDigitNotNull,
        'gpx_fix' => $this->faker->word,
        'gpx_time' => $this->faker->date('Y-m-d H:i:s'),
        '_type' => $this->faker->randomDigitNotNull,
        '_details' => $this->faker->word,
        'added_by_user_id' => $this->faker->word,
        'add_time' => $this->faker->date('Y-m-d H:i:s'),
        '_id_point_type' => $this->faker->word,
        'spatial_geometry' => $this->faker->word,
        'update_time' => $this->faker->date('Y-m-d H:i:s')
        ];
    }
}
