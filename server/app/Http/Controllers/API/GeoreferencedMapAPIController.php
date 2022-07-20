<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateGeoreferencedMapAPIRequest;
use App\Http\Requests\API\UpdateGeoreferencedMapAPIRequest;
use App\Models\GeoreferencedMap;
use App\Repositories\GeoreferencedMapRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class GeoreferencedMapController
 * @package App\Http\Controllers\API
 */

class GeoreferencedMapAPIController extends AppBaseController
{
    /** @var  GeoreferencedMapRepository */
    private $georeferencedMapRepository;

    public function __construct(GeoreferencedMapRepository $georeferencedMapRepo)
    {
        $this->georeferencedMapRepository = $georeferencedMapRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/georeferencedMaps",
     *      summary="getGeoreferencedMapList",
     *      tags={"GeoreferencedMap"},
     *      description="Get all GeoreferencedMaps",
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
     *                  @OA\Items(ref="#/definitions/GeoreferencedMap")
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
        $georeferencedMaps = $this->georeferencedMapRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($georeferencedMaps->toArray(), 'Georeferenced Maps retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/georeferencedMaps",
     *      summary="createGeoreferencedMap",
     *      tags={"GeoreferencedMap"},
     *      description="Create GeoreferencedMap",
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
     *                  ref="#/definitions/GeoreferencedMap"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateGeoreferencedMapAPIRequest $request)
    {
        $input = $request->all();

        $georeferencedMap = $this->georeferencedMapRepository->create($input);

        return $this->sendResponse($georeferencedMap->toArray(), 'Georeferenced Map saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/georeferencedMaps/{id}",
     *      summary="getGeoreferencedMapItem",
     *      tags={"GeoreferencedMap"},
     *      description="Get GeoreferencedMap",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of GeoreferencedMap",
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
     *                  ref="#/definitions/GeoreferencedMap"
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
        /** @var GeoreferencedMap $georeferencedMap */
        $georeferencedMap = $this->georeferencedMapRepository->find($id);

        if (empty($georeferencedMap)) {
            return $this->sendError('Georeferenced Map not found');
        }

        return $this->sendResponse($georeferencedMap->toArray(), 'Georeferenced Map retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/georeferencedMaps/{id}",
     *      summary="updateGeoreferencedMap",
     *      tags={"GeoreferencedMap"},
     *      description="Update GeoreferencedMap",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of GeoreferencedMap",
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
     *                  ref="#/definitions/GeoreferencedMap"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateGeoreferencedMapAPIRequest $request)
    {
        $input = $request->all();

        /** @var GeoreferencedMap $georeferencedMap */
        $georeferencedMap = $this->georeferencedMapRepository->find($id);

        if (empty($georeferencedMap)) {
            return $this->sendError('Georeferenced Map not found');
        }

        $georeferencedMap = $this->georeferencedMapRepository->update($input, $id);

        return $this->sendResponse($georeferencedMap->toArray(), 'GeoreferencedMap updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/georeferencedMaps/{id}",
     *      summary="deleteGeoreferencedMap",
     *      tags={"GeoreferencedMap"},
     *      description="Delete GeoreferencedMap",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of GeoreferencedMap",
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
        /** @var GeoreferencedMap $georeferencedMap */
        $georeferencedMap = $this->georeferencedMapRepository->find($id);

        if (empty($georeferencedMap)) {
            return $this->sendError('Georeferenced Map not found');
        }

        $georeferencedMap->delete();

        return $this->sendSuccess('Georeferenced Map deleted successfully');
    }
}
