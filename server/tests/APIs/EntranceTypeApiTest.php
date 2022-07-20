<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\EntranceType;

class EntranceTypeApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_entrance_type()
    {
        $entranceType = EntranceType::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/entrance_types', $entranceType
        );

        $this->assertApiResponse($entranceType);
    }

    /**
     * @test
     */
    public function test_read_entrance_type()
    {
        $entranceType = EntranceType::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/entrance_types/'.$entranceType->id
        );

        $this->assertApiResponse($entranceType->toArray());
    }

    /**
     * @test
     */
    public function test_update_entrance_type()
    {
        $entranceType = EntranceType::factory()->create();
        $editedEntranceType = EntranceType::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/entrance_types/'.$entranceType->id,
            $editedEntranceType
        );

        $this->assertApiResponse($editedEntranceType);
    }

    /**
     * @test
     */
    public function test_delete_entrance_type()
    {
        $entranceType = EntranceType::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/entrance_types/'.$entranceType->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/entrance_types/'.$entranceType->id
        );

        $this->response->assertStatus(404);
    }
}
