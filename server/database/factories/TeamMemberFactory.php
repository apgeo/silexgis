<?php

namespace Database\Factories;

use App\Models\TeamMember;
use Illuminate\Database\Eloquent\Factories\Factory;

class TeamMemberFactory extends Factory
{
    /**
     * The name of the factory's corresponding model.
     *
     * @var string
     */
    protected $model = TeamMember::class;

    /**
     * Define the model's default state.
     *
     * @return array
     */
    public function definition()
    {
        return [
            'first_name' => $this->faker->word,
        'last_name' => $this->faker->word,
        'nickname' => $this->faker->word,
        'group_id' => $this->faker->word,
        'picture_file_name' => $this->faker->word,
        'add_time' => $this->faker->date('Y-m-d H:i:s'),
        'description' => $this->faker->text,
        'email' => $this->faker->word,
        'phone_number' => $this->faker->word,
        'notes' => $this->faker->word,
        'connected_user_id' => $this->faker->word
        ];
    }
}
