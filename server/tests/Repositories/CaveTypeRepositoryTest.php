<?php namespace Tests\Repositories;

use App\Models\CaveType;
use App\Repositories\CaveTypeRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class CaveTypeRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var CaveTypeRepository
     */
    protected $caveTypeRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->caveTypeRepo = \App::make(CaveTypeRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_cave_type()
    {
        $caveType = CaveType::factory()->make()->toArray();

        $createdCaveType = $this->caveTypeRepo->create($caveType);

        $createdCaveType = $createdCaveType->toArray();
        $this->assertArrayHasKey('id', $createdCaveType);
        $this->assertNotNull($createdCaveType['id'], 'Created CaveType must have id specified');
        $this->assertNotNull(CaveType::find($createdCaveType['id']), 'CaveType with given id must be in DB');
        $this->assertModelData($caveType, $createdCaveType);
    }

    /**
     * @test read
     */
    public function test_read_cave_type()
    {
        $caveType = CaveType::factory()->create();

        $dbCaveType = $this->caveTypeRepo->find($caveType->id);

        $dbCaveType = $dbCaveType->toArray();
        $this->assertModelData($caveType->toArray(), $dbCaveType);
    }

    /**
     * @test update
     */
    public function test_update_cave_type()
    {
        $caveType = CaveType::factory()->create();
        $fakeCaveType = CaveType::factory()->make()->toArray();

        $updatedCaveType = $this->caveTypeRepo->update($fakeCaveType, $caveType->id);

        $this->assertModelData($fakeCaveType, $updatedCaveType->toArray());
        $dbCaveType = $this->caveTypeRepo->find($caveType->id);
        $this->assertModelData($fakeCaveType, $dbCaveType->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_cave_type()
    {
        $caveType = CaveType::factory()->create();

        $resp = $this->caveTypeRepo->delete($caveType->id);

        $this->assertTrue($resp);
        $this->assertNull(CaveType::find($caveType->id), 'CaveType should not exist in DB');
    }
}
