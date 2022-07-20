<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\GeoreferencedMap;

class GeoreferencedMapApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_georeferenced_map()
    {
        $georeferencedMap = GeoreferencedMap::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/georeferenced_maps', $georeferencedMap
        );

        $this->assertApiResponse($georeferencedMap);
    }

    /**
     * @test
     */
    public function test_read_georeferenced_map()
    {
        $georeferencedMap = GeoreferencedMap::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/georeferenced_maps/'.$georeferencedMap->id
        );

        $this->assertApiResponse($georeferencedMap->toArray());
    }

    /**
     * @test
     */
    public function test_update_georeferenced_map()
    {
        $georeferencedMap = GeoreferencedMap::factory()->create();
        $editedGeoreferencedMap = GeoreferencedMap::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/georeferenced_maps/'.$georeferencedMap->id,
            $editedGeoreferencedMap
        );

        $this->assertApiResponse($editedGeoreferencedMap);
    }

    /**
     * @test
     */
    public function test_delete_georeferenced_map()
    {
        $georeferencedMap = GeoreferencedMap::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/georeferenced_maps/'.$georeferencedMap->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/georeferenced_maps/'.$georeferencedMap->id
        );

        $this->response->assertStatus(404);
    }
}
