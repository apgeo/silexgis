<?php namespace Tests\Repositories;

use App\Models\File;
use App\Repositories\FileRepository;
use Illuminate\Foundation\Testing\DatabaseTransactions;
use Tests\TestCase;
use Tests\ApiTestTrait;

class FileRepositoryTest extends TestCase
{
    use ApiTestTrait, DatabaseTransactions;

    /**
     * @var FileRepository
     */
    protected $fileRepo;

    public function setUp() : void
    {
        parent::setUp();
        $this->fileRepo = \App::make(FileRepository::class);
    }

    /**
     * @test create
     */
    public function test_create_file()
    {
        $file = File::factory()->make()->toArray();

        $createdFile = $this->fileRepo->create($file);

        $createdFile = $createdFile->toArray();
        $this->assertArrayHasKey('id', $createdFile);
        $this->assertNotNull($createdFile['id'], 'Created File must have id specified');
        $this->assertNotNull(File::find($createdFile['id']), 'File with given id must be in DB');
        $this->assertModelData($file, $createdFile);
    }

    /**
     * @test read
     */
    public function test_read_file()
    {
        $file = File::factory()->create();

        $dbFile = $this->fileRepo->find($file->id);

        $dbFile = $dbFile->toArray();
        $this->assertModelData($file->toArray(), $dbFile);
    }

    /**
     * @test update
     */
    public function test_update_file()
    {
        $file = File::factory()->create();
        $fakeFile = File::factory()->make()->toArray();

        $updatedFile = $this->fileRepo->update($fakeFile, $file->id);

        $this->assertModelData($fakeFile, $updatedFile->toArray());
        $dbFile = $this->fileRepo->find($file->id);
        $this->assertModelData($fakeFile, $dbFile->toArray());
    }

    /**
     * @test delete
     */
    public function test_delete_file()
    {
        $file = File::factory()->create();

        $resp = $this->fileRepo->delete($file->id);

        $this->assertTrue($resp);
        $this->assertNull(File::find($file->id), 'File should not exist in DB');
    }
}
