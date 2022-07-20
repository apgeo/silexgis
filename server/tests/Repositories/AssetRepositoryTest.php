<?php namespace Tests\Repositories;

use App\Models\Asset;
use App\Repositories\AssetRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class AssetRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var AssetRepository
     */
    protected $assetRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->assetRepo = \App::make(AssetRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_asset()
    {
        $asset = Asset::factory()->make()->toArray();

        $createdAsset = $this->assetRepo->create($asset);

        $createdAsset = $createdAsset->toArray();
        $this->assertArrayHasKey('id', $createdAsset);
        $this->assertNotNull($createdAsset['id'], 'Created Asset must have id specified');
        $this->assertNotNull(Asset::find($createdAsset['id']), 'Asset with given id must be in DB');
        $this->assertModelData($asset, $createdAsset);
    }

    /**
     * @test read
     */
    public function test_read_asset()
    {
        $asset = Asset::factory()->create();

        $dbAsset = $this->assetRepo->find($asset->id);

        $dbAsset = $dbAsset->toArray();
        $this->assertModelData($asset->toArray(), $dbAsset);
    }

    /**
     * @test update
     */
    public function test_update_asset()
    {
        $asset = Asset::factory()->create();
        $fakeAsset = Asset::factory()->make()->toArray();

        $updatedAsset = $this->assetRepo->update($fakeAsset, $asset->id);

        $this->assertModelData($fakeAsset, $updatedAsset->toArray());
        $dbAsset = $this->assetRepo->find($asset->id);
        $this->assertModelData($fakeAsset, $dbAsset->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_asset()
    {
        $asset = Asset::factory()->create();

        $resp = $this->assetRepo->delete($asset->id);

        $this->assertTrue($resp);
        $this->assertNull(Asset::find($asset->id), 'Asset should not exist in DB');
    }
}
