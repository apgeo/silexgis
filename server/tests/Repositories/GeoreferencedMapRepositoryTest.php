<?php namespace Tests\Repositories;

use App\Models\GeoreferencedMap;
use App\Repositories\GeoreferencedMapRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class GeoreferencedMapRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var GeoreferencedMapRepository
     */
    protected $georeferencedMapRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->georeferencedMapRepo = \App::make(GeoreferencedMapRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_georeferenced_map()
    {
        $georeferencedMap = GeoreferencedMap::factory()->make()->toArray();

        $createdGeoreferencedMap = $this->georeferencedMapRepo->create($georeferencedMap);

        $createdGeoreferencedMap = $createdGeoreferencedMap->toArray();
        $this->assertArrayHasKey('id', $createdGeoreferencedMap);
        $this->assertNotNull($createdGeoreferencedMap['id'], 'Created GeoreferencedMap must have id specified');
        $this->assertNotNull(GeoreferencedMap::find($createdGeoreferencedMap['id']), 'GeoreferencedMap with given id must be in DB');
        $this->assertModelData($georeferencedMap, $createdGeoreferencedMap);
    }

    /**
     * @test read
     */
    public function test_read_georeferenced_map()
    {
        $georeferencedMap = GeoreferencedMap::factory()->create();

        $dbGeoreferencedMap = $this->georeferencedMapRepo->find($georeferencedMap->id);

        $dbGeoreferencedMap = $dbGeoreferencedMap->toArray();
        $this->assertModelData($georeferencedMap->toArray(), $dbGeoreferencedMap);
    }

    /**
     * @test update
     */
    public function test_update_georeferenced_map()
    {
        $georeferencedMap = GeoreferencedMap::factory()->create();
        $fakeGeoreferencedMap = GeoreferencedMap::factory()->make()->toArray();

        $updatedGeoreferencedMap = $this->georeferencedMapRepo->update($fakeGeoreferencedMap, $georeferencedMap->id);

        $this->assertModelData($fakeGeoreferencedMap, $updatedGeoreferencedMap->toArray());
        $dbGeoreferencedMap = $this->georeferencedMapRepo->find($georeferencedMap->id);
        $this->assertModelData($fakeGeoreferencedMap, $dbGeoreferencedMap->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_georeferenced_map()
    {
        $georeferencedMap = GeoreferencedMap::factory()->create();

        $resp = $this->georeferencedMapRepo->delete($georeferencedMap->id);

        $this->assertTrue($resp);
        $this->assertNull(GeoreferencedMap::find($georeferencedMap->id), 'GeoreferencedMap should not exist in DB');
    }
}
