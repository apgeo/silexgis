<?php namespace Tests\Repositories;

use App\Models\CaveEntrance;
use App\Repositories\CaveEntranceRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class CaveEntranceRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var CaveEntranceRepository
     */
    protected $caveEntranceRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->caveEntranceRepo = \App::make(CaveEntranceRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_cave_entrance()
    {
        $caveEntrance = CaveEntrance::factory()->make()->toArray();

        $createdCaveEntrance = $this->caveEntranceRepo->create($caveEntrance);

        $createdCaveEntrance = $createdCaveEntrance->toArray();
        $this->assertArrayHasKey('id', $createdCaveEntrance);
        $this->assertNotNull($createdCaveEntrance['id'], 'Created CaveEntrance must have id specified');
        $this->assertNotNull(CaveEntrance::find($createdCaveEntrance['id']), 'CaveEntrance with given id must be in DB');
        $this->assertModelData($caveEntrance, $createdCaveEntrance);
    }

    /**
     * @test read
     */
    public function test_read_cave_entrance()
    {
        $caveEntrance = CaveEntrance::factory()->create();

        $dbCaveEntrance = $this->caveEntranceRepo->find($caveEntrance->id);

        $dbCaveEntrance = $dbCaveEntrance->toArray();
        $this->assertModelData($caveEntrance->toArray(), $dbCaveEntrance);
    }

    /**
     * @test update
     */
    public function test_update_cave_entrance()
    {
        $caveEntrance = CaveEntrance::factory()->create();
        $fakeCaveEntrance = CaveEntrance::factory()->make()->toArray();

        $updatedCaveEntrance = $this->caveEntranceRepo->update($fakeCaveEntrance, $caveEntrance->id);

        $this->assertModelData($fakeCaveEntrance, $updatedCaveEntrance->toArray());
        $dbCaveEntrance = $this->caveEntranceRepo->find($caveEntrance->id);
        $this->assertModelData($fakeCaveEntrance, $dbCaveEntrance->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_cave_entrance()
    {
        $caveEntrance = CaveEntrance::factory()->create();

        $resp = $this->caveEntranceRepo->delete($caveEntrance->id);

        $this->assertTrue($resp);
        $this->assertNull(CaveEntrance::find($caveEntrance->id), 'CaveEntrance should not exist in DB');
    }
}
