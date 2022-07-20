<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateCaveAPIRequest;
use App\Http\Requests\API\UpdateCaveAPIRequest;
use App\Models\Cave;
use App\Repositories\CaveRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

use Illuminate\Support\Facades\DB;
// use Illuminate\Support\Facades\Log;

/**
 * Class CaveController
 * @package App\Http\Controllers\API
 */

class CaveAPIController extends AppBaseController
{
    /** @var  CaveRepository */
    private $caveRepository;

    public function __construct(CaveRepository $caveRepo)
    {
        $this->caveRepository = $caveRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/caves",
     *      summary="getCaveList",
     *      tags={"Cave"},
     *      description="Get all Caves",
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
     *                  @OA\Items(ref="#/definitions/Cave")
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
        $caves = $this->caveRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($caves->toArray(), 'Caves retrieved successfully');
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/caves",
     *      summary="createCave",
     *      tags={"Cave"},
     *      description="Create Cave",
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
     *                  ref="#/definitions/Cave"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(
        CreateCaveAPIRequest $request,
        \App\Repositories\CaveEntranceRepository $caveEntranceRepository,
        \App\Repositories\FeatureRepository $featureRepository
    ) {
        try {
            $input = $request->all();

            // $input["created_at"] = now(); // date(time());
            // $input["updated_at"] = now();
            // $input["created_at"] = $input["created_at"] ? null : now();
            // $input["updated_at"] = $input["updated_at"] ? null : now();

            DB::beginTransaction();

            $cave = $this->caveRepository->create($input);
            
            foreach ($input['entrances'] as $ce) {
                // print_r($ce);
                $latitude = $ce['coordinates']['1'];
                $longitude = $ce['coordinates']['0'];

                // $successFlag = DB::insert("insert into geometries (spatial_geometry) VALUES (ST_SetSRID(ST_MakePoint($longitude, $latitude), 4326 ))");
                // or ST_SetSRID(ST_MakePoint(long, lat), 4326);
                // or ST_GeomFromText('ST_Point($latitude $longitude)', 4326) // they called this "silly"
                // geometry vs geography                
                
                // $point_geometry_id = DB::insert("SELECT CURRVAL(pg_get_serial_sequence('geometries','id'));");
                
                $point_geometry_id = DB::table('geometries')->insertGetId(
                    array('spatial_geometry' => DB::raw("ST_SetSRID(ST_MakePoint($longitude, $latitude), 4326 )"))
                    ,'id');

                $cave_entrance_feature_type = DB::table('feature_types')
                        ->select('id')
                        ->where('name', 'cave_entrance')
                        ->get();

                $feature = $featureRepository->create(
                    array(
                        'name' => $ce['title'], // 'cave entrance',
                        'point_geometry_id' => $point_geometry_id,
                        'feature_type_id' => $cave_entrance_feature_type[0]->id
                    )
                );

                $caveEntrance = $caveEntranceRepository->create(
                        array(
                            "name" => $ce['title'],
                            "entrance_type" => 0,
                            "cave_id" => $cave->id,
                            "is_main_entrance" => 0, //--
                            "feature_id" => $feature->id
                            // "created_at" => DB::raw("NOW()"),
                            // "updated_at" => DB::raw("NOW()")
                        )
                );
            }

            DB::commit();

            return $this->sendResponse($cave->toArray(), 'Cave saved successfully');

        } catch (\Illuminate\Database\QueryException $e) {
            DB::rollback();

            $error_code = $e->errorInfo[1];

            if (($error_code == 1062) // primary key or unique key already exists
                || ($error_code == 1452) // Integrity constraint violation: 1452 Cannot add or update a child row: a foreign key constraint fails 
            ) {
                // self::delete($lid);
                return json_encode(array(
                    'status' => 409,
                    'message' => "this record is duplicate" // id={$request->all()['id']}
                ));
            } else
                throw $e;
        }
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/caves/{id}",
     *      summary="getCaveItem",
     *      tags={"Cave"},
     *      description="Get Cave",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Cave",
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
     *                  ref="#/definitions/Cave"
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
        /** @var Cave $cave */
        $cave = $this->caveRepository->find($id);

        if (empty($cave)) {
            return $this->sendError('Cave not found');
        }

        return $this->sendResponse($cave->toArray(), 'Cave retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/caves/{id}",
     *      summary="updateCave",
     *      tags={"Cave"},
     *      description="Update Cave",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Cave",
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
     *                  ref="#/definitions/Cave"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateCaveAPIRequest $request)
    {
        $input = $request->all();

        /** @var Cave $cave */
        $cave = $this->caveRepository->find($id);

        if (empty($cave)) {
            return $this->sendError('Cave not found');
        }

        $cave = $this->caveRepository->update($input, $id);

        return $this->sendResponse($cave->toArray(), 'Cave updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/caves/{id}",
     *      summary="deleteCave",
     *      tags={"Cave"},
     *      description="Delete Cave",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Cave",
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
        /** @var Cave $cave */
        $cave = $this->caveRepository->find($id);

        if (empty($cave)) {
            return $this->sendError('Cave not found');
        }

        $cave->delete();

        return $this->sendSuccess('Cave deleted successfully');
    }
}
