<?php namespace Tests\Repositories;

use App\Models\Feature;
use App\Repositories\FeatureRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class FeatureRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var FeatureRepository
     */
    protected $featureRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->featureRepo = \App::make(FeatureRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_feature()
    {
        $feature = Feature::factory()->make()->toArray();

        $createdFeature = $this->featureRepo->create($feature);

        $createdFeature = $createdFeature->toArray();
        $this->assertArrayHasKey('id', $createdFeature);
        $this->assertNotNull($createdFeature['id'], 'Created Feature must have id specified');
        $this->assertNotNull(Feature::find($createdFeature['id']), 'Feature with given id must be in DB');
        $this->assertModelData($feature, $createdFeature);
    }

    /**
     * @test read
     */
    public function test_read_feature()
    {
        $feature = Feature::factory()->create();

        $dbFeature = $this->featureRepo->find($feature->id);

        $dbFeature = $dbFeature->toArray();
        $this->assertModelData($feature->toArray(), $dbFeature);
    }

    /**
     * @test update
     */
    public function test_update_feature()
    {
        $feature = Feature::factory()->create();
        $fakeFeature = Feature::factory()->make()->toArray();

        $updatedFeature = $this->featureRepo->update($fakeFeature, $feature->id);

        $this->assertModelData($fakeFeature, $updatedFeature->toArray());
        $dbFeature = $this->featureRepo->find($feature->id);
        $this->assertModelData($fakeFeature, $dbFeature->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_feature()
    {
        $feature = Feature::factory()->create();

        $resp = $this->featureRepo->delete($feature->id);

        $this->assertTrue($resp);
        $this->assertNull(Feature::find($feature->id), 'Feature should not exist in DB');
    }
}
