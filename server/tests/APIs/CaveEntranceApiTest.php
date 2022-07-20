<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\CaveEntrance;

class CaveEntranceApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_cave_entrance()
    {
        $caveEntrance = CaveEntrance::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/cave_entrances', $caveEntrance
        );

        $this->assertApiResponse($caveEntrance);
    }

    /**
     * @test
     */
    public function test_read_cave_entrance()
    {
        $caveEntrance = CaveEntrance::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/cave_entrances/'.$caveEntrance->id
        );

        $this->assertApiResponse($caveEntrance->toArray());
    }

    /**
     * @test
     */
    public function test_update_cave_entrance()
    {
        $caveEntrance = CaveEntrance::factory()->create();
        $editedCaveEntrance = CaveEntrance::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/cave_entrances/'.$caveEntrance->id,
            $editedCaveEntrance
        );

        $this->assertApiResponse($editedCaveEntrance);
    }

    /**
     * @test
     */
    public function test_delete_cave_entrance()
    {
        $caveEntrance = CaveEntrance::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/cave_entrances/'.$caveEntrance->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/cave_entrances/'.$caveEntrance->id
        );

        $this->response->assertStatus(404);
    }
}
