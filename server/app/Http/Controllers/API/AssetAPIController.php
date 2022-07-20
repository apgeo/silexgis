<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateAssetAPIRequest;
use App\Http\Requests\API\UpdateAssetAPIRequest;
use App\Models\Asset;
use App\Repositories\AssetRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class AssetController
 * @package App\Http\Controllers\API
 */

class AssetAPIController extends AppBaseController
{
    /** @var  AssetRepository */
    private $assetRepository;

    public function __construct(AssetRepository $assetRepo)
    {
        $this->assetRepository = $assetRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/assets",
     *      summary="getAssetList",
     *      tags={"Asset"},
     *      description="Get all Assets",
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  type="array",
     *                  @OA\Items(ref="#/definitions/Asset")
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function index(Request $request)
    {
        $assets = $this->assetRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($assets->toArray(), 'Assets retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/assets",
     *      summary="createAsset",
     *      tags={"Asset"},
     *      description="Create Asset",
     *      @OA\RequestBody(
     *        required=true,
     *        @OA\MediaType(
     *            mediaType="application/x-www-form-urlencoded",
     *            @OA\Schema(
     *                type="object",
     *                required={""},
     *                @OA\Property(
     *                    property="name",
     *                    description="desc",
     *                    type="string"
     *                )
     *            )
     *        )
     *      ),
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  ref="#/definitions/Asset"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateAssetAPIRequest $request)
    {
        $input = $request->all();

        $asset = $this->assetRepository->create($input);

        return $this->sendResponse($asset->toArray(), 'Asset saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/assets/{id}",
     *      summary="getAssetItem",
     *      tags={"Asset"},
     *      description="Get Asset",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Asset",
     *           @OA\Schema(
     *             type="integer"
     *          ),
     *          required=true,
     *          in="path"
     *      ),
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  ref="#/definitions/Asset"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function show($id)
    {
        /** @var Asset $asset */
        $asset = $this->assetRepository->find($id);

        if (empty($asset)) {
            return $this->sendError('Asset not found');
        }

        return $this->sendResponse($asset->toArray(), 'Asset retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/assets/{id}",
     *      summary="updateAsset",
     *      tags={"Asset"},
     *      description="Update Asset",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Asset",
     *           @OA\Schema(
     *             type="integer"
     *          ),
     *          required=true,
     *          in="path"
     *      ),
     *      @OA\RequestBody(
     *        required=true,
     *        @OA\MediaType(
     *            mediaType="application/x-www-form-urlencoded",
     *            @OA\Schema(
     *                type="object",
     *                required={""},
     *                @OA\Property(
     *                    property="name",
     *                    description="desc",
     *                    type="string"
     *                )
     *            )
     *        )
     *      ),
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  ref="#/definitions/Asset"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateAssetAPIRequest $request)
    {
        $input = $request->all();

        /** @var Asset $asset */
        $asset = $this->assetRepository->find($id);

        if (empty($asset)) {
            return $this->sendError('Asset not found');
        }

        $asset = $this->assetRepository->update($input, $id);

        return $this->sendResponse($asset->toArray(), 'Asset updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/assets/{id}",
     *      summary="deleteAsset",
     *      tags={"Asset"},
     *      description="Delete Asset",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Asset",
     *           @OA\Schema(
     *             type="integer"
     *          ),
     *          required=true,
     *          in="path"
     *      ),
     *      @OA\Response(
     *          response=200,
     *          description="successful operation",
     *          @OA\Schema(
     *              type="object",
     *              @OA\Property(
     *                  property="success",
     *                  type="boolean"
     *              ),
     *              @OA\Property(
     *                  property="data",
     *                  type="string"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function destroy($id)
    {
        /** @var Asset $asset */
        $asset = $this->assetRepository->find($id);

        if (empty($asset)) {
            return $this->sendError('Asset not found');
        }

        $asset->delete();

        return $this->sendSuccess('Asset deleted successfully');
    }
}
