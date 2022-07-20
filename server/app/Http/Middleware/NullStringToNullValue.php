<?php

namespace App\Http\Middleware;

use Illuminate\Database\Eloquent\Model;
use Closure;

// this is fix for converting values that come as "null" to null, otherwise we get big nasty cave bugs everywhere
// plus all rules like << 'surveyed_length' => 'nullable|integer', >> blast validation errors because they get a string "null" instead of number or a null value
class NullStringToNullValue //  extends Middleware
{
    /**
     * Handle an incoming request.
     *
     * @param  \Illuminate\Http\Request  $request
     * @param  \Closure  $next
     * @return mixed
     */
    public function handle($request, Closure $next)
    {
        // echo "NullStringToNullValue.handle()";
        
        // $output = $next($request);
        
        // var_dump($output);
        // echo "x";
        $input = $request->all();
        
        foreach($input as $key=>$value)
        {
            if($value == "null")
            {
                $input[$key] = null;
            }
        }

        $request->replace($input);

        // if($output instanceof Model)
        //     return response()->json(array_map(function ($value) {
        //         return $value === null ? '' : $value;
        //     }, $output->toArray()));

        $output = $next($request);

        return $output;
    }
}