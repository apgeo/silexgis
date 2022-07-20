<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\Geofile;

class GeofileApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_geofile()
    {
        $geofile = Geofile::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/geofiles', $geofile
        );

        $this->assertApiResponse($geofile);
    }

    /**
     * @test
     */
    public function test_read_geofile()
    {
        $geofile = Geofile::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/geofiles/'.$geofile->id
        );

        $this->assertApiResponse($geofile->toArray());
    }

    /**
     * @test
     */
    public function test_update_geofile()
    {
        $geofile = Geofile::factory()->create();
        $editedGeofile = Geofile::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/geofiles/'.$geofile->id,
            $editedGeofile
        );

        $this->assertApiResponse($editedGeofile);
    }

    /**
     * @test
     */
    public function test_delete_geofile()
    {
        $geofile = Geofile::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/geofiles/'.$geofile->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/geofiles/'.$geofile->id
        );

        $this->response->assertStatus(404);
    }
}
