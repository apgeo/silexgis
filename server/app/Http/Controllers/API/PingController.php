<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
// use App\Http\Controllers\AppBaseController;
use Illuminate\Support\Carbon;

// class PingController extends AppBaseController
class PingController extends Controller
{
    public function index()
    {
        return response()->json([
            'status' => 'ok',
            'timestamp' => Carbon::now(),
            'host' => request()->ip(),
        ]);
    }
}
