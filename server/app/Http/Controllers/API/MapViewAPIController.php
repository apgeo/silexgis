<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateMapViewAPIRequest;
use App\Http\Requests\API\UpdateMapViewAPIRequest;
use App\Models\MapView;
use App\Repositories\MapViewRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class MapViewController
 * @package App\Http\Controllers\API
 */

class MapViewAPIController extends AppBaseController
{
    /** @var  MapViewRepository */
    private $mapViewRepository;

    public function __construct(MapViewRepository $mapViewRepo)
    {
        $this->mapViewRepository = $mapViewRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/mapViews",
     *      summary="getMapViewList",
     *      tags={"MapView"},
     *      description="Get all MapViews",
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
     *                  @OA\Items(ref="#/definitions/MapView")
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
        $mapViews = $this->mapViewRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($mapViews->toArray(), 'Map Views retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/mapViews",
     *      summary="createMapView",
     *      tags={"MapView"},
     *      description="Create MapView",
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
     *                  ref="#/definitions/MapView"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateMapViewAPIRequest $request)
    {
        $input = $request->all();

        $mapView = $this->mapViewRepository->create($input);

        return $this->sendResponse($mapView->toArray(), 'Map View saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/mapViews/{id}",
     *      summary="getMapViewItem",
     *      tags={"MapView"},
     *      description="Get MapView",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of MapView",
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
     *                  ref="#/definitions/MapView"
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
        /** @var MapView $mapView */
        $mapView = $this->mapViewRepository->find($id);

        if (empty($mapView)) {
            return $this->sendError('Map View not found');
        }

        return $this->sendResponse($mapView->toArray(), 'Map View retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/mapViews/{id}",
     *      summary="updateMapView",
     *      tags={"MapView"},
     *      description="Update MapView",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of MapView",
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
     *                  ref="#/definitions/MapView"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateMapViewAPIRequest $request)
    {
        $input = $request->all();

        /** @var MapView $mapView */
        $mapView = $this->mapViewRepository->find($id);

        if (empty($mapView)) {
            return $this->sendError('Map View not found');
        }

        $mapView = $this->mapViewRepository->update($input, $id);

        return $this->sendResponse($mapView->toArray(), 'MapView updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/mapViews/{id}",
     *      summary="deleteMapView",
     *      tags={"MapView"},
     *      description="Delete MapView",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of MapView",
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
        /** @var MapView $mapView */
        $mapView = $this->mapViewRepository->find($id);

        if (empty($mapView)) {
            return $this->sendError('Map View not found');
        }

        $mapView->delete();

        return $this->sendSuccess('Map View deleted successfully');
    }
}
