<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\Cave;

class CaveApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_cave()
    {
        $cave = Cave::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/caves', $cave
        );

        $this->assertApiResponse($cave);
    }

    /**
     * @test
     */
    public function test_read_cave()
    {
        $cave = Cave::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/caves/'.$cave->id
        );

        $this->assertApiResponse($cave->toArray());
    }

    /**
     * @test
     */
    public function test_update_cave()
    {
        $cave = Cave::factory()->create();
        $editedCave = Cave::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/caves/'.$cave->id,
            $editedCave
        );

        $this->assertApiResponse($editedCave);
    }

    /**
     * @test
     */
    public function test_delete_cave()
    {
        $cave = Cave::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/caves/'.$cave->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/caves/'.$cave->id
        );

        $this->response->assertStatus(404);
    }
}
