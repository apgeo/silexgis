<?php

namespace Database\Factories;

use App\Models\TripLogs;
use Illuminate\Database\Eloquent\Factories\Factory;

class TripLogsFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = TripLogs::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'add_time' => $this->faker->date('Y-m-d H:i:s'),
        'trip_start_time' => $this->faker->date('Y-m-d H:i:s'),
        'trip_end_time' => $this->faker->date('Y-m-d H:i:s'),
        'details' => $this->faker->text,
        'target_zone' => $this->faker->word,
        'type' => $this->faker->word,
        'temporary' => $this->faker->word,
        'summary' => $this->faker->word,
        'title' => $this->faker->word
        ];
    }
}
