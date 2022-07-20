<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\CaveType;

class CaveTypeApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_cave_type()
    {
        $caveType = CaveType::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/cave_types', $caveType
        );

        $this->assertApiResponse($caveType);
    }

    /**
     * @test
     */
    public function test_read_cave_type()
    {
        $caveType = CaveType::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/cave_types/'.$caveType->id
        );

        $this->assertApiResponse($caveType->toArray());
    }

    /**
     * @test
     */
    public function test_update_cave_type()
    {
        $caveType = CaveType::factory()->create();
        $editedCaveType = CaveType::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/cave_types/'.$caveType->id,
            $editedCaveType
        );

        $this->assertApiResponse($editedCaveType);
    }

    /**
     * @test
     */
    public function test_delete_cave_type()
    {
        $caveType = CaveType::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/cave_types/'.$caveType->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/cave_types/'.$caveType->id
        );

        $this->response->assertStatus(404);
    }
}
