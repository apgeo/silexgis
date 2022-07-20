<?php namespace Tests\Repositories;

use App\Models\Image;
use App\Repositories\ImageRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class ImageRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var ImageRepository
     */
    protected $imageRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->imageRepo = \App::make(ImageRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_image()
    {
        $image = Image::factory()->make()->toArray();

        $createdImage = $this->imageRepo->create($image);

        $createdImage = $createdImage->toArray();
        $this->assertArrayHasKey('id', $createdImage);
        $this->assertNotNull($createdImage['id'], 'Created Image must have id specified');
        $this->assertNotNull(Image::find($createdImage['id']), 'Image with given id must be in DB');
        $this->assertModelData($image, $createdImage);
    }

    /**
     * @test read
     */
    public function test_read_image()
    {
        $image = Image::factory()->create();

        $dbImage = $this->imageRepo->find($image->id);

        $dbImage = $dbImage->toArray();
        $this->assertModelData($image->toArray(), $dbImage);
    }

    /**
     * @test update
     */
    public function test_update_image()
    {
        $image = Image::factory()->create();
        $fakeImage = Image::factory()->make()->toArray();

        $updatedImage = $this->imageRepo->update($fakeImage, $image->id);

        $this->assertModelData($fakeImage, $updatedImage->toArray());
        $dbImage = $this->imageRepo->find($image->id);
        $this->assertModelData($fakeImage, $dbImage->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_image()
    {
        $image = Image::factory()->create();

        $resp = $this->imageRepo->delete($image->id);

        $this->assertTrue($resp);
        $this->assertNull(Image::find($image->id), 'Image should not exist in DB');
    }
}
