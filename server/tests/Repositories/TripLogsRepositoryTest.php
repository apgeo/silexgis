<?php namespace Tests\Repositories;

use App\Models\TripLogs;
use App\Repositories\TripLogsRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class TripLogsRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var TripLogsRepository
     */
    protected $tripLogsRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->tripLogsRepo = \App::make(TripLogsRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_trip_logs()
    {
        $tripLogs = TripLogs::factory()->make()->toArray();

        $createdTripLogs = $this->tripLogsRepo->create($tripLogs);

        $createdTripLogs = $createdTripLogs->toArray();
        $this->assertArrayHasKey('id', $createdTripLogs);
        $this->assertNotNull($createdTripLogs['id'], 'Created TripLogs must have id specified');
        $this->assertNotNull(TripLogs::find($createdTripLogs['id']), 'TripLogs with given id must be in DB');
        $this->assertModelData($tripLogs, $createdTripLogs);
    }

    /**
     * @test read
     */
    public function test_read_trip_logs()
    {
        $tripLogs = TripLogs::factory()->create();

        $dbTripLogs = $this->tripLogsRepo->find($tripLogs->id);

        $dbTripLogs = $dbTripLogs->toArray();
        $this->assertModelData($tripLogs->toArray(), $dbTripLogs);
    }

    /**
     * @test update
     */
    public function test_update_trip_logs()
    {
        $tripLogs = TripLogs::factory()->create();
        $fakeTripLogs = TripLogs::factory()->make()->toArray();

        $updatedTripLogs = $this->tripLogsRepo->update($fakeTripLogs, $tripLogs->id);

        $this->assertModelData($fakeTripLogs, $updatedTripLogs->toArray());
        $dbTripLogs = $this->tripLogsRepo->find($tripLogs->id);
        $this->assertModelData($fakeTripLogs, $dbTripLogs->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_trip_logs()
    {
        $tripLogs = TripLogs::factory()->create();

        $resp = $this->tripLogsRepo->delete($tripLogs->id);

        $this->assertTrue($resp);
        $this->assertNull(TripLogs::find($tripLogs->id), 'TripLogs should not exist in DB');
    }
}
