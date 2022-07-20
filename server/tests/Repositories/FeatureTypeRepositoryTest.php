<?php namespace Tests\Repositories;

use App\Models\FeatureType;
use App\Repositories\FeatureTypeRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class FeatureTypeRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var FeatureTypeRepository
     */
    protected $featureTypeRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->featureTypeRepo = \App::make(FeatureTypeRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_feature_type()
    {
        $featureType = FeatureType::factory()->make()->toArray();

        $createdFeatureType = $this->featureTypeRepo->create($featureType);

        $createdFeatureType = $createdFeatureType->toArray();
        $this->assertArrayHasKey('id', $createdFeatureType);
        $this->assertNotNull($createdFeatureType['id'], 'Created FeatureType must have id specified');
        $this->assertNotNull(FeatureType::find($createdFeatureType['id']), 'FeatureType with given id must be in DB');
        $this->assertModelData($featureType, $createdFeatureType);
    }

    /**
     * @test read
     */
    public function test_read_feature_type()
    {
        $featureType = FeatureType::factory()->create();

        $dbFeatureType = $this->featureTypeRepo->find($featureType->id);

        $dbFeatureType = $dbFeatureType->toArray();
        $this->assertModelData($featureType->toArray(), $dbFeatureType);
    }

    /**
     * @test update
     */
    public function test_update_feature_type()
    {
        $featureType = FeatureType::factory()->create();
        $fakeFeatureType = FeatureType::factory()->make()->toArray();

        $updatedFeatureType = $this->featureTypeRepo->update($fakeFeatureType, $featureType->id);

        $this->assertModelData($fakeFeatureType, $updatedFeatureType->toArray());
        $dbFeatureType = $this->featureTypeRepo->find($featureType->id);
        $this->assertModelData($fakeFeatureType, $dbFeatureType->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_feature_type()
    {
        $featureType = FeatureType::factory()->create();

        $resp = $this->featureTypeRepo->delete($featureType->id);

        $this->assertTrue($resp);
        $this->assertNull(FeatureType::find($featureType->id), 'FeatureType should not exist in DB');
    }
}
