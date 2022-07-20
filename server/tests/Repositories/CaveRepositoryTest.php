<?php namespace Tests\Repositories;

use App\Models\Cave;
use App\Repositories\CaveRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class CaveRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var CaveRepository
     */
    protected $caveRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->caveRepo = \App::make(CaveRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_cave()
    {
        $cave = Cave::factory()->make()->toArray();

        $createdCave = $this->caveRepo->create($cave);

        $createdCave = $createdCave->toArray();
        $this->assertArrayHasKey('id', $createdCave);
        $this->assertNotNull($createdCave['id'], 'Created Cave must have id specified');
        $this->assertNotNull(Cave::find($createdCave['id']), 'Cave with given id must be in DB');
        $this->assertModelData($cave, $createdCave);
    }

    /**
     * @test read
     */
    public function test_read_cave()
    {
        $cave = Cave::factory()->create();

        $dbCave = $this->caveRepo->find($cave->id);

        $dbCave = $dbCave->toArray();
        $this->assertModelData($cave->toArray(), $dbCave);
    }

    /**
     * @test update
     */
    public function test_update_cave()
    {
        $cave = Cave::factory()->create();
        $fakeCave = Cave::factory()->make()->toArray();

        $updatedCave = $this->caveRepo->update($fakeCave, $cave->id);

        $this->assertModelData($fakeCave, $updatedCave->toArray());
        $dbCave = $this->caveRepo->find($cave->id);
        $this->assertModelData($fakeCave, $dbCave->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_cave()
    {
        $cave = Cave::factory()->create();

        $resp = $this->caveRepo->delete($cave->id);

        $this->assertTrue($resp);
        $this->assertNull(Cave::find($cave->id), 'Cave should not exist in DB');
    }
}
