<?php namespace Tests\Repositories;

use App\Models\EntranceType;
use App\Repositories\EntranceTypeRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class EntranceTypeRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var EntranceTypeRepository
     */
    protected $entranceTypeRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->entranceTypeRepo = \App::make(EntranceTypeRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_entrance_type()
    {
        $entranceType = EntranceType::factory()->make()->toArray();

        $createdEntranceType = $this->entranceTypeRepo->create($entranceType);

        $createdEntranceType = $createdEntranceType->toArray();
        $this->assertArrayHasKey('id', $createdEntranceType);
        $this->assertNotNull($createdEntranceType['id'], 'Created EntranceType must have id specified');
        $this->assertNotNull(EntranceType::find($createdEntranceType['id']), 'EntranceType with given id must be in DB');
        $this->assertModelData($entranceType, $createdEntranceType);
    }

    /**
     * @test read
     */
    public function test_read_entrance_type()
    {
        $entranceType = EntranceType::factory()->create();

        $dbEntranceType = $this->entranceTypeRepo->find($entranceType->id);

        $dbEntranceType = $dbEntranceType->toArray();
        $this->assertModelData($entranceType->toArray(), $dbEntranceType);
    }

    /**
     * @test update
     */
    public function test_update_entrance_type()
    {
        $entranceType = EntranceType::factory()->create();
        $fakeEntranceType = EntranceType::factory()->make()->toArray();

        $updatedEntranceType = $this->entranceTypeRepo->update($fakeEntranceType, $entranceType->id);

        $this->assertModelData($fakeEntranceType, $updatedEntranceType->toArray());
        $dbEntranceType = $this->entranceTypeRepo->find($entranceType->id);
        $this->assertModelData($fakeEntranceType, $dbEntranceType->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_entrance_type()
    {
        $entranceType = EntranceType::factory()->create();

        $resp = $this->entranceTypeRepo->delete($entranceType->id);

        $this->assertTrue($resp);
        $this->assertNull(EntranceType::find($entranceType->id), 'EntranceType should not exist in DB');
    }
}
