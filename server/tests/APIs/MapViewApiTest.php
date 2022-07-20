<?php namespace Tests\APIs;

use Illuminate\Foundation\Testing\WithoutMiddleware;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;
use App\Models\MapView;

class MapViewApiTest extends TestCase
{
    use ApiTestTrait, WithoutMiddleware, DatabaseTransactions;

    /**
     * @test
     */
    public function test_create_map_view()
    {
        $mapView = MapView::factory()->make()->toArray();

        $this->response = $this->json(
            'POST',
            '/api/map_views', $mapView
        );

        $this->assertApiResponse($mapView);
    }

    /**
     * @test
     */
    public function test_read_map_view()
    {
        $mapView = MapView::factory()->create();

        $this->response = $this->json(
            'GET',
            '/api/map_views/'.$mapView->id
        );

        $this->assertApiResponse($mapView->toArray());
    }

    /**
     * @test
     */
    public function test_update_map_view()
    {
        $mapView = MapView::factory()->create();
        $editedMapView = MapView::factory()->make()->toArray();

        $this->response = $this->json(
            'PUT',
            '/api/map_views/'.$mapView->id,
            $editedMapView
        );

        $this->assertApiResponse($editedMapView);
    }

    /**
     * @test
     */
    public function test_delete_map_view()
    {
        $mapView = MapView::factory()->create();

        $this->response = $this->json(
            'DELETE',
             '/api/map_views/'.$mapView->id
         );

        $this->assertApiSuccess();
        $this->response = $this->json(
            'GET',
            '/api/map_views/'.$mapView->id
        );

        $this->response->assertStatus(404);
    }
}
