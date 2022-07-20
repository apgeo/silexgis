<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateTripLogsAPIRequest;
use App\Http\Requests\API\UpdateTripLogsAPIRequest;
use App\Models\TripLogs;
use App\Repositories\TripLogsRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

/**
 * Class TripLogsController
 * @package App\Http\Controllers\API
 */

class TripLogsAPIController extends AppBaseController
{
    /** @var  TripLogsRepository */
    private $tripLogsRepository;

    public function __construct(TripLogsRepository $tripLogsRepo)
    {
        $this->tripLogsRepository = $tripLogsRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/tripLogs",
     *      summary="getTripLogsList",
     *      tags={"TripLogs"},
     *      description="Get all TripLogs",
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
     *                  @OA\Items(ref="#/definitions/TripLogs")
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
        $tripLogs = $this->tripLogsRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($tripLogs->toArray(), 'Trip Logs retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/tripLogs",
     *      summary="createTripLogs",
     *      tags={"TripLogs"},
     *      description="Create TripLogs",
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
     *                  ref="#/definitions/TripLogs"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateTripLogsAPIRequest $request)
    {
        $input = $request->all();

        $tripLogs = $this->tripLogsRepository->create($input);

        return $this->sendResponse($tripLogs->toArray(), 'Trip Logs saved successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/tripLogs/{id}",
     *      summary="getTripLogsItem",
     *      tags={"TripLogs"},
     *      description="Get TripLogs",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of TripLogs",
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
     *                  ref="#/definitions/TripLogs"
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
        /** @var TripLogs $tripLogs */
        $tripLogs = $this->tripLogsRepository->find($id);

        if (empty($tripLogs)) {
            return $this->sendError('Trip Logs not found');
        }

        return $this->sendResponse($tripLogs->toArray(), 'Trip Logs retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/tripLogs/{id}",
     *      summary="updateTripLogs",
     *      tags={"TripLogs"},
     *      description="Update TripLogs",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of TripLogs",
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
     *                  ref="#/definitions/TripLogs"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateTripLogsAPIRequest $request)
    {
        $input = $request->all();

        /** @var TripLogs $tripLogs */
        $tripLogs = $this->tripLogsRepository->find($id);

        if (empty($tripLogs)) {
            return $this->sendError('Trip Logs not found');
        }

        $tripLogs = $this->tripLogsRepository->update($input, $id);

        return $this->sendResponse($tripLogs->toArray(), 'TripLogs updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/tripLogs/{id}",
     *      summary="deleteTripLogs",
     *      tags={"TripLogs"},
     *      description="Delete TripLogs",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of TripLogs",
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
        /** @var TripLogs $tripLogs */
        $tripLogs = $this->tripLogsRepository->find($id);

        if (empty($tripLogs)) {
            return $this->sendError('Trip Logs not found');
        }

        $tripLogs->delete();

        return $this->sendSuccess('Trip Logs deleted successfully');
    }
}
