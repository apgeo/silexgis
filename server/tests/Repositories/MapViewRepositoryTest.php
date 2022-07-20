<?php namespace Tests\Repositories;

use App\Models\MapView;
use App\Repositories\MapViewRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class MapViewRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var MapViewRepository
     */
    protected $mapViewRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->mapViewRepo = \App::make(MapViewRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_map_view()
    {
        $mapView = MapView::factory()->make()->toArray();

        $createdMapView = $this->mapViewRepo->create($mapView);

        $createdMapView = $createdMapView->toArray();
        $this->assertArrayHasKey('id', $createdMapView);
        $this->assertNotNull($createdMapView['id'], 'Created MapView must have id specified');
        $this->assertNotNull(MapView::find($createdMapView['id']), 'MapView with given id must be in DB');
        $this->assertModelData($mapView, $createdMapView);
    }

    /**
     * @test read
     */
    public function test_read_map_view()
    {
        $mapView = MapView::factory()->create();

        $dbMapView = $this->mapViewRepo->find($mapView->id);

        $dbMapView = $dbMapView->toArray();
        $this->assertModelData($mapView->toArray(), $dbMapView);
    }

    /**
     * @test update
     */
    public function test_update_map_view()
    {
        $mapView = MapView::factory()->create();
        $fakeMapView = MapView::factory()->make()->toArray();

        $updatedMapView = $this->mapViewRepo->update($fakeMapView, $mapView->id);

        $this->assertModelData($fakeMapView, $updatedMapView->toArray());
        $dbMapView = $this->mapViewRepo->find($mapView->id);
        $this->assertModelData($fakeMapView, $dbMapView->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_map_view()
    {
        $mapView = MapView::factory()->create();

        $resp = $this->mapViewRepo->delete($mapView->id);

        $this->assertTrue($resp);
        $this->assertNull(MapView::find($mapView->id), 'MapView should not exist in DB');
    }
}
