<?php namespace Tests\Repositories;

use App\Models\Geofile;
use App\Repositories\GeofileRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class GeofileRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var GeofileRepository
     */
    protected $geofileRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->geofileRepo = \App::make(GeofileRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_geofile()
    {
        $geofile = Geofile::factory()->make()->toArray();

        $createdGeofile = $this->geofileRepo->create($geofile);

        $createdGeofile = $createdGeofile->toArray();
        $this->assertArrayHasKey('id', $createdGeofile);
        $this->assertNotNull($createdGeofile['id'], 'Created Geofile must have id specified');
        $this->assertNotNull(Geofile::find($createdGeofile['id']), 'Geofile with given id must be in DB');
        $this->assertModelData($geofile, $createdGeofile);
    }

    /**
     * @test read
     */
    public function test_read_geofile()
    {
        $geofile = Geofile::factory()->create();

        $dbGeofile = $this->geofileRepo->find($geofile->id);

        $dbGeofile = $dbGeofile->toArray();
        $this->assertModelData($geofile->toArray(), $dbGeofile);
    }

    /**
     * @test update
     */
    public function test_update_geofile()
    {
        $geofile = Geofile::factory()->create();
        $fakeGeofile = Geofile::factory()->make()->toArray();

        $updatedGeofile = $this->geofileRepo->update($fakeGeofile, $geofile->id);

        $this->assertModelData($fakeGeofile, $updatedGeofile->toArray());
        $dbGeofile = $this->geofileRepo->find($geofile->id);
        $this->assertModelData($fakeGeofile, $dbGeofile->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_geofile()
    {
        $geofile = Geofile::factory()->create();

        $resp = $this->geofileRepo->delete($geofile->id);

        $this->assertTrue($resp);
        $this->assertNull(Geofile::find($geofile->id), 'Geofile should not exist in DB');
    }
}
