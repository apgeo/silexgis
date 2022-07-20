<?php

namespace App\Http\Controllers\API;

use App\Http\Requests\API\CreateFeatureAPIRequest;
use App\Http\Requests\API\UpdateFeatureAPIRequest;
use App\Models\Feature;
use App\Repositories\FeatureRepository;
use Illuminate\Http\Request;
use App\Http\Controllers\AppBaseController;
use Response;

use Illuminate\Support\Facades\DB;
// use Illuminate\Support\Facades\Log;

/**
 * Class FeatureController
 * @package App\Http\Controllers\API
 */

class FeatureAPIController extends AppBaseController
{
    /** @var  FeatureRepository */
    private $featureRepository;

    public function __construct(FeatureRepository $featureRepo)
    {
        $this->featureRepository = $featureRepo;
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Get(
     *      path="/features",
     *      summary="getFeatureList",
     *      tags={"Feature"},
     *      description="Get all Features",
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
     *                  @OA\Items(ref="#/definitions/Feature")
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
        /*
        $features = $this->featureRepository->all(
            $request->except(['skip', 'limit']),
            $request->get('skip'),
            $request->get('limit')
        );

        return $this->sendResponse($features->toArray(), 'Features retrieved successfully');        
        */
        // $id = +$id;

        $features = DB::select("
        SELECT features.id as id, 
        features.*,
        
        geometries.id as geometry_id,
        ST_AsText(geometries.spatial_geometry, 7) as geometry_wkt

        FROM features
        INNER JOIN geometries ON features.point_geometry_id = geometries.id")
            // ->get()
            // ->toArray()
            ;
     
        return $this->sendResponse($features/*->toArray()*/, 'Features retrieved successfully');

        /*
        //-- JSON_NUMERIC_CHECK might not be good since it will convert all numeric like values to numeric types, even if that might have string vealue in the database
        //-- maybe a better solution is to check what is the problem with MySQL/PHP adapter or to cast them here one by one to ensure correct data type

        return json_encode($dosarOutMappedData, JSON_NUMERIC_CHECK);
        */
    }

    /**
     * @param Request $request
     * @return Response
     *
     * @OA\Post(
     *      path="/features",
     *      summary="createFeature",
     *      tags={"Feature"},
     *      description="Create Feature",
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
     *                  ref="#/definitions/Feature"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function store(CreateFeatureAPIRequest $request)
    {
        try {
            $input = $request->all();

            // $input["created_at"] = $input["created_at"] ? null : now();
            // $input["updated_at"] = $input["updated_at"] ? null : now();

            DB::beginTransaction();
            
            $point_geometry_id = null;

            if (array_key_exists('point_geometry_id', $input) && !empty($point_geometry_id))
                $point_geometry_id = $input['point_geometry_id'];
            else            
            {
                $geometryWKT = $input['geometryWKT'];
                // $longitude latitude = $input['geometry'];

                // $featureType = $this->point   Repository->create([
                //     'spatial_geometry' => DB::raw("ST_GeomFromText('$geometryWKT', 4326 )")
                // ]);
                
                // $successFlag = DB::insert("insert into geometries (spatial_geometry) VALUES (ST_GeomFromText('$geometryWKT', 4326 ));"); // this returns boolean indicating sucess, not an id
                // $point_geometry_id = DB::insert("SELECT CURRVAL(pg_get_serial_sequence('geometries','id'));");
                
                $point_geometry_id =  DB::table('geometries')->insertGetId(
                    array('spatial_geometry' => DB::raw("ST_GeomFromText('$geometryWKT', 4326 )"))
                    ,'id');
                // $point_geometry_id = DB::insert("insert into geometries (spatial_geometry) VALUES (ST_SetSRID(ST_MakePoint($longitude, $latitude), 4326 ))");

                // $point_geometry_id = DB::insert("insert into geometries (id, spatial_geometry) VALUES (DEFAULT, ST_GeomFromText('$geometryWKT', 4326 )) RETURNING id"); // returning not working, proabably either because of the autoincrement or because it is run
            }

            $input['point_geometry_id'] = $point_geometry_id;
            
            $feature = $this->featureRepository->create($input);
            // or 
            // $featureType = $this->featureTypeRepository->create(
            //     array_merge($input, [$point_geometry_id]) // not tested
            // );           

            DB::commit();

            return $this->sendResponse($feature->toArray(), 'Feature saved successfully');

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
        }    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Get(
     *      path="/features/{id}",
     *      summary="getFeatureItem",
     *      tags={"Feature"},
     *      description="Get Feature",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Feature",
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
     *                  ref="#/definitions/Feature"
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
        /** @var Feature $feature */
        $feature = $this->featureRepository->find($id);

        if (empty($feature)) {
            return $this->sendError('Feature not found');
        }

        return $this->sendResponse($feature->toArray(), 'Feature retrieved successfully');
    }

    /**
     * @param int $id
     * @param Request $request
     * @return Response
     *
     * @OA\Put(
     *      path="/features/{id}",
     *      summary="updateFeature",
     *      tags={"Feature"},
     *      description="Update Feature",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Feature",
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
     *                  ref="#/definitions/Feature"
     *              ),
     *              @OA\Property(
     *                  property="message",
     *                  type="string"
     *              )
     *          )
     *      )
     * )
     */
    public function update($id, UpdateFeatureAPIRequest $request)
    {
        $input = $request->all();

        /** @var Feature $feature */
        $feature = $this->featureRepository->find($id);

        if (empty($feature)) {
            return $this->sendError('Feature not found');
        }

        $feature = $this->featureRepository->update($input, $id);

        return $this->sendResponse($feature->toArray(), 'Feature updated successfully');
    }

    /**
     * @param int $id
     * @return Response
     *
     * @OA\Delete(
     *      path="/features/{id}",
     *      summary="deleteFeature",
     *      tags={"Feature"},
     *      description="Delete Feature",
     *      @OA\Parameter(
     *          name="id",
     *          description="id of Feature",
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
        /** @var Feature $feature */
        $feature = $this->featureRepository->find($id);

        if (empty($feature)) {
            return $this->sendError('Feature not found');
        }

        $feature->delete();

        return $this->sendSuccess('Feature deleted successfully');
    }
}
