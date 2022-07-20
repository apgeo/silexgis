<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateFeatureTypeAPIRequest;
use App\Http\Requests\API\UpdateFeatureTypeAPIRequest;
use App\Models\FeatureType;
use App\Repositories\FeatureTypeRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class FeatureTypeController
 * @package App\Http\Controllers\API
 */

class FeatureTypeAPIController extends AppBaseController
{
    /** @var  FeatureTypeRepository */
    private $featureTypeRepository;

    public function __construct(FeatureTypeRepository $featureTypeRepo)
    {
        $this->featureTypeRepository = $featureTypeRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/featureTypes",
     *      summary="getFeatureTypeList",
     *      tags={"FeatureType"},
     *      description="Get all FeatureTypes",
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
     *                  @OA\Items(ref="#/definitions/FeatureType")
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
        $featureTypes = $this->featureTypeRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        )
        ->sortBy('id')->values(); // or sortByDesc // ->orderBy not working on this object type
        // sortBy changes the output schema by making key value pairs for rows instead of only values so "->values()" needs to be added

        return $this->sendResponse($featureTypes->toArray(), 'Feature Types retrieved successfully');
        // return $this->sendResponse($featureTypes->toArray(), 'Feature Types retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/featureTypes",
     *      summary="createFeatureType",
     *      tags={"FeatureType"},
     *      description="Create FeatureType",
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
     *                  ref="#/definitions/FeatureType"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateFeatureTypeAPIRequest $request)
    {       
        $input = $request->all();

        $featureType = $this->featureTypeRepository->create($input);

        return $this->sendResponse($featureType->toArray(), 'Feature Type saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/featureTypes/{id}",
     *      summary="getFeatureTypeItem",
     *      tags={"FeatureType"},
     *      description="Get FeatureType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of FeatureType",
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
     *                  ref="#/definitions/FeatureType"
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
        /** @var FeatureType $featureType */
        $featureType = $this->featureTypeRepository->find($id);

        if (empty($featureType)) {
            return $this->sendError('Feature Type not found');
        }

        return $this->sendResponse($featureType->toArray(), 'Feature Type retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/featureTypes/{id}",
     *      summary="updateFeatureType",
     *      tags={"FeatureType"},
     *      description="Update FeatureType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of FeatureType",
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
     *                  ref="#/definitions/FeatureType"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateFeatureTypeAPIRequest $request)
    {
        $input = $request->all();

        /** @var FeatureType $featureType */
        $featureType = $this->featureTypeRepository->find($id);

        if (empty($featureType)) {
            return $this->sendError('Feature Type not found');
        }

        $featureType = $this->featureTypeRepository->update($input, $id);

        return $this->sendResponse($featureType->toArray(), 'FeatureType updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/featureTypes/{id}",
     *      summary="deleteFeatureType",
     *      tags={"FeatureType"},
     *      description="Delete FeatureType",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of FeatureType",
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
        /** @var FeatureType $featureType */
        $featureType = $this->featureTypeRepository->find($id);

        if (empty($featureType)) {
            return $this->sendError('Feature Type not found');
        }

        $featureType->delete();

        return $this->sendSuccess('Feature Type deleted successfully');
    }
}
