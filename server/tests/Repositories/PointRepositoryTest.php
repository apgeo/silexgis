<?php namespace Tests\Repositories;

use App\Models\Point;
use App\Repositories\PointRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class PointRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var PointRepository
     */
    protected $pointRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->pointRepo = \App::make(PointRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_point()
    {
        $point = Point::factory()->make()->toArray();

        $createdPoint = $this->pointRepo->create($point);

        $createdPoint = $createdPoint->toArray();
        $this->assertArrayHasKey('id', $createdPoint);
        $this->assertNotNull($createdPoint['id'], 'Created Point must have id specified');
        $this->assertNotNull(Point::find($createdPoint['id']), 'Point with given id must be in DB');
        $this->assertModelData($point, $createdPoint);
    }

    /**
     * @test read
     */
    public function test_read_point()
    {
        $point = Point::factory()->create();

        $dbPoint = $this->pointRepo->find($point->id);

        $dbPoint = $dbPoint->toArray();
        $this->assertModelData($point->toArray(), $dbPoint);
    }

    /**
     * @test update
     */
    public function test_update_point()
    {
        $point = Point::factory()->create();
        $fakePoint = Point::factory()->make()->toArray();

        $updatedPoint = $this->pointRepo->update($fakePoint, $point->id);

        $this->assertModelData($fakePoint, $updatedPoint->toArray());
        $dbPoint = $this->pointRepo->find($point->id);
        $this->assertModelData($fakePoint, $dbPoint->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_point()
    {
        $point = Point::factory()->create();

        $resp = $this->pointRepo->delete($point->id);

        $this->assertTrue($resp);
        $this->assertNull(Point::find($point->id), 'Point should not exist in DB');
    }
}
